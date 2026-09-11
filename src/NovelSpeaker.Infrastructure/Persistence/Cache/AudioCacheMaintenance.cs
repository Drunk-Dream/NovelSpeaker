using NovelSpeaker.Application.Cache;
using NovelSpeaker.Infrastructure.FileSystem.Cache;
using NovelSpeaker.Infrastructure.Speech.Http;

namespace NovelSpeaker.Infrastructure.Persistence.Cache;

/// <summary>
/// Repairs index/file drift and applies the configured least-recently-used limit.
/// </summary>
internal sealed class AudioCacheMaintenance
{
    private readonly SqliteAudioCacheIndex _index;
    private readonly AudioCacheFileStore _fileStore;
    private readonly IAudioCacheLimitProvider _limitProvider;
    private readonly IAudioCacheProtectionRegistry _protectionRegistry;
    private readonly AudioProbe _audioProbe;
    private readonly IChapterSpeechPlanStore _speechPlanStore;
    private readonly TimeProvider _timeProvider;
    private static readonly TimeSpan LongUnusedValidationAge = TimeSpan.FromDays(30);

    public AudioCacheMaintenance(
        SqliteAudioCacheIndex index,
        AudioCacheFileStore fileStore,
        IAudioCacheLimitProvider limitProvider,
        IAudioCacheProtectionRegistry protectionRegistry,
        IChapterSpeechPlanStore speechPlanStore,
        TimeProvider? timeProvider = null,
        AudioProbe? audioProbe = null)
    {
        _index = index;
        _fileStore = fileStore;
        _limitProvider = limitProvider;
        _protectionRegistry = protectionRegistry;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _audioProbe = audioProbe ?? new AudioProbe();
        _speechPlanStore = speechPlanStore ?? throw new ArgumentNullException(nameof(speechPlanStore));
    }

    public async Task<bool> RunAsync(
        CancellationToken cancellationToken,
        bool cleanupOrphanedPlans,
        Action<CacheInvalidation>? invalidation = null)
    {
        var changed = false;
        _fileStore.DeleteResidualTemporaryFiles(cancellationToken);

        var entries = await _index.GetMaintenanceEntriesAsync(cancellationToken).ConfigureAwait(false);
        var knownPaths = new HashSet<string>(GetPathComparer());
        var now = _timeProvider.GetUtcNow();
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string filePath;
            try
            {
                filePath = _fileStore.ResolveIndexedPath(entry.FilePath);
            }
            catch (InvalidDataException)
            {
                await _index.RemoveAsync(entry.CacheKey, cancellationToken).ConfigureAwait(false);
                changed = true;
                PublishChapterChange(entry, invalidation);
                continue;
            }

            knownPaths.Add(filePath);
            if (!_fileStore.Exists(filePath))
            {
                await _index.RemoveAsync(entry.CacheKey, cancellationToken).ConfigureAwait(false);
                changed = true;
                PublishChapterChange(entry, invalidation);
                continue;
            }

            if (ShouldValidate(entry, now) && !_audioProbe.CanDecode(filePath))
            {
                await _index.RemoveAsync(entry.CacheKey, cancellationToken).ConfigureAwait(false);
                _fileStore.TryDeleteFile(filePath);
                changed = true;
                PublishChapterChange(entry, invalidation);
                continue;
            }

            if (ShouldValidate(entry, now))
            {
                await _index.MarkValidatedAsync(entry.CacheKey, now, cancellationToken).ConfigureAwait(false);
            }
        }

        if (_fileStore.DeleteOrphanCacheFiles(knownPaths, cancellationToken))
        {
            changed = true;
            invalidation?.Invoke(CacheInvalidation.ForGlobal(AllPhysicalAspects));
        }

        var limitChanged = await EnforceLimitAsync(
            cancellationToken,
            change => invalidation?.Invoke(change)).ConfigureAwait(false);
        if (cleanupOrphanedPlans && await _speechPlanStore
                .DeletePlansWithoutCacheEntriesAsync(cancellationToken)
                .ConfigureAwait(false) > 0)
        {
            changed = true;
            invalidation?.Invoke(CacheInvalidation.ForGlobal(CacheInvalidationAspect.Coverage));
        }

        return changed || limitChanged;
    }

    public async Task<bool> EnforceLimitAsync(
        CancellationToken cancellationToken,
        Action<CacheInvalidation>? invalidation = null)
    {
        var limitBytes = _limitProvider.GetCurrentLimitBytes();
        var summary = await _index.GetSummaryAsync(cancellationToken).ConfigureAwait(false);
        if (summary.TotalSizeBytes <= limitBytes)
        {
            return false;
        }

        var totalSize = summary.TotalSizeBytes;
        var changed = false;
        var entries = await _index.GetLruEntriesAsync(cancellationToken).ConfigureAwait(false);
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (totalSize <= limitBytes)
            {
                break;
            }

            var filePath = _fileStore.ResolveIndexedPath(entry.FilePath);
            if (_protectionRegistry.IsProtected(filePath))
            {
                continue;
            }

            if (!_fileStore.Exists(filePath) || _fileStore.TryDeleteFile(filePath))
            {
                await _index.RemoveAsync(entry.CacheKey, cancellationToken).ConfigureAwait(false);
                totalSize = Math.Max(0, totalSize - entry.FileSize);
                changed = true;
                PublishChapterChange(entry, invalidation);
            }
        }

        return changed;
    }

    public long GetCurrentLimitBytes() => _limitProvider.GetCurrentLimitBytes();

    private static bool ShouldValidate(AudioCacheMaintenanceEntry entry, DateTimeOffset now) =>
        now - entry.LastAccessedAt >= LongUnusedValidationAge &&
        (entry.ValidatedAt is null || now - entry.ValidatedAt.Value >= LongUnusedValidationAge);

    private static void PublishChapterChange(
        AudioCacheMaintenanceEntry entry,
        Action<CacheInvalidation>? invalidation)
    {
        if (entry.ChapterIndex is int chapterIndex)
        {
            invalidation?.Invoke(CacheInvalidation.ForChapters(
                entry.BookId,
                [chapterIndex],
                AllPhysicalAspects));
        }
        else
        {
            invalidation?.Invoke(CacheInvalidation.ForBook(entry.BookId, AllPhysicalAspects));
        }
    }

    private static void PublishChapterChange(
        AudioCacheIndexEntry entry,
        Action<CacheInvalidation>? invalidation)
    {
        if (entry.BookId is null)
        {
            invalidation?.Invoke(CacheInvalidation.ForGlobal(AllPhysicalAspects));
        }
        else if (entry.ChapterIndex is int chapterIndex)
        {
            invalidation?.Invoke(CacheInvalidation.ForChapters(
                entry.BookId,
                [chapterIndex],
                AllPhysicalAspects));
        }
        else
        {
            invalidation?.Invoke(CacheInvalidation.ForBook(entry.BookId, AllPhysicalAspects));
        }
    }

    private const CacheInvalidationAspect AllPhysicalAspects =
        CacheInvalidationAspect.PhysicalSummary |
        CacheInvalidationAspect.CatalogStructure |
        CacheInvalidationAspect.Coverage;

    private static StringComparer GetPathComparer()
    {
        return OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
    }
}
