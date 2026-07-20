using NovelSpeaker.Application.Playback;
using NovelSpeaker.Infrastructure.FileSystem.Cache;

namespace NovelSpeaker.Infrastructure.Persistence.Playback;

/// <summary>
/// Serializes cache operations and composes the index, file store and maintenance collaborators.
/// </summary>
internal sealed class AudioCacheFacade : IAudioCache, IAudioCacheStore
{
    private readonly SqliteAudioCacheIndex _index;
    private readonly AudioCacheFileStore _fileStore;
    private readonly AudioCacheMaintenance _maintenance;
    private readonly IAudioCacheProtectionRegistry _protectionRegistry;
    private readonly SemaphoreSlim _mutex = new(1, 1);

    public AudioCacheFacade(
        SqliteAudioCacheIndex index,
        AudioCacheFileStore fileStore,
        AudioCacheMaintenance maintenance,
        IAudioCacheProtectionRegistry protectionRegistry)
    {
        _index = index;
        _fileStore = fileStore;
        _maintenance = maintenance;
        _protectionRegistry = protectionRegistry;
    }

    public Task<AudioCacheEntry?> TryGetAsync(AudioCacheKey key, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        return RunExclusiveAsync(ct => TryGetCoreAsync(key, ct), cancellationToken);
    }

    public Task<AudioCacheEntry> StoreAsync(AudioCacheWriteRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return RunExclusiveAsync(ct => StoreCoreAsync(request, ct), cancellationToken);
    }

    public Task InvalidateAsync(AudioCacheKey key, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        return RunExclusiveAsync(ct => InvalidateCoreAsync(key, ct), cancellationToken);
    }

    public Task<AudioCacheStoreSummary> GetSummaryAsync(CancellationToken cancellationToken)
    {
        return RunExclusiveAsync(GetSummaryCoreAsync, cancellationToken);
    }

    public Task<IReadOnlyList<CachedBookStoreSummary>> GetBooksAsync(CancellationToken cancellationToken)
    {
        return RunExclusiveAsync(_index.GetBooksAsync, cancellationToken);
    }

    public Task<IReadOnlyList<CachedChapterStoreSummary>> GetChaptersAsync(
        string bookId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookId);
        return RunExclusiveAsync(ct => _index.GetChaptersAsync(bookId, ct), cancellationToken);
    }

    public Task<AudioCacheStoreCleanupResult> ClearChapterAsync(
        string bookId,
        int chapterIndex,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookId);
        return RunExclusiveAsync(
            ct => ClearEntriesCoreAsync(bookId, chapterIndex, ct),
            cancellationToken);
    }

    public Task<AudioCacheStoreCleanupResult> ClearBookAsync(
        string bookId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookId);
        return RunExclusiveAsync(
            ct => ClearEntriesCoreAsync(bookId, chapterIndex: null, ct),
            cancellationToken);
    }

    public Task<AudioCacheStoreCleanupResult> ClearAllAsync(CancellationToken cancellationToken)
    {
        return RunExclusiveAsync(ClearAllCoreAsync, cancellationToken);
    }

    public Task RunMaintenanceAsync(CancellationToken cancellationToken)
    {
        return RunExclusiveAsync(_maintenance.RunAsync, cancellationToken);
    }

    private async Task<AudioCacheEntry?> TryGetCoreAsync(
        AudioCacheKey key,
        CancellationToken cancellationToken)
    {
        var entry = await _index.FindAsync(key, cancellationToken).ConfigureAwait(false);
        if (entry is null)
        {
            return null;
        }

        var filePath = _fileStore.ResolveIndexedPath(entry.FilePath);
        if (!_fileStore.Exists(filePath))
        {
            await _index.RemoveAsync(entry.CacheKey, cancellationToken).ConfigureAwait(false);
            return null;
        }

        await _index.TouchAsync(
            entry.CacheKey,
            _fileStore.GetStorageKey(filePath),
            cancellationToken).ConfigureAwait(false);
        return new AudioCacheEntry(key, filePath);
    }

    private async Task<AudioCacheEntry> StoreCoreAsync(
        AudioCacheWriteRequest request,
        CancellationToken cancellationToken)
    {
        var destinationPath = _fileStore.GetDestinationPath(request);
        using var finalProtection = _protectionRegistry.Protect(destinationPath);
        var file = await _fileStore.StoreAsync(request, cancellationToken).ConfigureAwait(false);
        await _index.UpsertAsync(request, file.StorageKey, file.FileSize, cancellationToken).ConfigureAwait(false);
        await _maintenance.EnforceLimitAsync(cancellationToken).ConfigureAwait(false);
        return new AudioCacheEntry(request.Key, file.FilePath);
    }

    private async Task InvalidateCoreAsync(AudioCacheKey key, CancellationToken cancellationToken)
    {
        var entry = await _index.FindAsync(key, cancellationToken).ConfigureAwait(false);
        if (entry is null)
        {
            return;
        }

        var filePath = _fileStore.ResolveIndexedPath(entry.FilePath);
        await _index.RemoveAsync(entry.CacheKey, cancellationToken).ConfigureAwait(false);
        _fileStore.TryDeleteFile(filePath);
    }

    private async Task<AudioCacheStoreSummary> GetSummaryCoreAsync(CancellationToken cancellationToken)
    {
        var summary = await _index.GetSummaryAsync(cancellationToken).ConfigureAwait(false);
        var limitBytes = _maintenance.GetCurrentLimitBytes();
        return new AudioCacheStoreSummary(
            summary.TotalSizeBytes,
            summary.EntryCount,
            limitBytes,
            summary.TotalSizeBytes > limitBytes);
    }

    private async Task<AudioCacheStoreCleanupResult> ClearEntriesCoreAsync(
        string? bookId,
        int? chapterIndex,
        CancellationToken cancellationToken)
    {
        var entries = await _index.GetEntriesAsync(bookId, chapterIndex, cancellationToken).ConfigureAwait(false);
        var deletedBytes = 0L;
        var deletedEntryCount = 0;
        var protectedEntryCount = 0;
        var failedEntryCount = 0;

        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var filePath = _fileStore.ResolveIndexedPath(entry.FilePath);
            if (_protectionRegistry.IsProtected(filePath))
            {
                protectedEntryCount++;
                continue;
            }

            if (!_fileStore.Exists(filePath) || _fileStore.TryDeleteFile(filePath))
            {
                await _index.RemoveAsync(entry.CacheKey, cancellationToken).ConfigureAwait(false);
                deletedBytes += entry.FileSize;
                deletedEntryCount++;
            }
            else
            {
                failedEntryCount++;
            }
        }

        return new AudioCacheStoreCleanupResult(
            deletedBytes,
            deletedEntryCount,
            protectedEntryCount,
            failedEntryCount);
    }

    private async Task<AudioCacheStoreCleanupResult> ClearAllCoreAsync(CancellationToken cancellationToken)
    {
        var result = await ClearEntriesCoreAsync(null, null, cancellationToken).ConfigureAwait(false);
        _fileStore.DeleteResidualTemporaryFiles(cancellationToken);
        _fileStore.DeleteOrphanCacheFiles(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            cancellationToken);
        return result;
    }

    private async Task<T> RunExclusiveAsync<T>(
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken)
    {
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await action(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _mutex.Release();
        }
    }

    private async Task RunExclusiveAsync(
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken)
    {
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await action(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _mutex.Release();
        }
    }
}
