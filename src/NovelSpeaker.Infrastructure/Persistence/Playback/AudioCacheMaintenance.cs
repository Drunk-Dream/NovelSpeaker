using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Playback.Cache;
using NovelSpeaker.Infrastructure.FileSystem.Cache;

namespace NovelSpeaker.Infrastructure.Persistence.Playback;

/// <summary>
/// Repairs index/file drift and applies the configured least-recently-used limit.
/// </summary>
internal sealed class AudioCacheMaintenance
{
    private readonly SqliteAudioCacheIndex _index;
    private readonly AudioCacheFileStore _fileStore;
    private readonly IAudioCacheLimitProvider _limitProvider;
    private readonly IAudioCacheProtectionRegistry _protectionRegistry;

    public AudioCacheMaintenance(
        SqliteAudioCacheIndex index,
        AudioCacheFileStore fileStore,
        IAudioCacheLimitProvider limitProvider,
        IAudioCacheProtectionRegistry protectionRegistry)
    {
        _index = index;
        _fileStore = fileStore;
        _limitProvider = limitProvider;
        _protectionRegistry = protectionRegistry;
    }

    public async Task<bool> RunAsync(
        CancellationToken cancellationToken,
        Action? cacheChanged = null)
    {
        var changed = false;
        _fileStore.DeleteResidualTemporaryFiles(cancellationToken);

        var entries = await _index.GetAllEntriesAsync(cancellationToken).ConfigureAwait(false);
        var knownPaths = new HashSet<string>(GetPathComparer());
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var filePath = _fileStore.ResolveIndexedPath(entry.FilePath);
            knownPaths.Add(filePath);
            if (!_fileStore.Exists(filePath))
            {
                await _index.RemoveAsync(entry.CacheKey, cancellationToken).ConfigureAwait(false);
                changed = true;
                cacheChanged?.Invoke();
            }
        }

        _fileStore.DeleteOrphanCacheFiles(knownPaths, cancellationToken);
        return await EnforceLimitAsync(cancellationToken, cacheChanged).ConfigureAwait(false) || changed;
    }

    public async Task<bool> EnforceLimitAsync(
        CancellationToken cancellationToken,
        Action? cacheChanged = null)
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
                cacheChanged?.Invoke();
            }
        }

        return changed;
    }

    public long GetCurrentLimitBytes() => _limitProvider.GetCurrentLimitBytes();

    private static StringComparer GetPathComparer()
    {
        return OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
    }
}
