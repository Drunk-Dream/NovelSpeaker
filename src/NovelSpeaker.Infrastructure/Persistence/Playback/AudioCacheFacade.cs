using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Playback.Cache;
using NovelSpeaker.Infrastructure.FileSystem.Cache;
using NovelSpeaker.Infrastructure.Speech.Http;

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
    private readonly AudioProbe _audioProbe;
    private readonly SemaphoreSlim _mutex = new(1, 1);

    public AudioCacheFacade(
        SqliteAudioCacheIndex index,
        AudioCacheFileStore fileStore,
        AudioCacheMaintenance maintenance,
        IAudioCacheProtectionRegistry protectionRegistry,
        AudioProbe audioProbe)
    {
        _index = index;
        _fileStore = fileStore;
        _maintenance = maintenance;
        _protectionRegistry = protectionRegistry;
        _audioProbe = audioProbe;
    }

    public event EventHandler<CacheChangedEventArgs>? Changed;

    public async Task<AudioCacheEntry?> TryGetAsync(AudioCacheKey key, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        var result = await RunExclusiveAsync(ct => TryGetCoreAsync(key, ct), cancellationToken).ConfigureAwait(false);
        if (result.RemovedStaleEntry)
        {
            OnChanged(null, null);
        }

        return result.Entry;
    }

    public async Task<AudioCacheEntry> StoreAsync(AudioCacheWriteRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var changed = false;
        var maintenanceChanged = false;
        try
        {
            var result = await RunExclusiveAsync(
                ct => StoreCoreAsync(
                    request,
                    () => changed = true,
                    () =>
                    {
                        changed = true;
                        maintenanceChanged = true;
                    },
                    ct),
                cancellationToken).ConfigureAwait(false);
            return result;
        }
        finally
        {
            if (changed)
            {
                OnChanged(
                    maintenanceChanged ? null : request.BookId,
                    maintenanceChanged ? null : request.ChapterIndex);
            }
        }
    }

    public async Task InvalidateAsync(AudioCacheKey key, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        var changed = await RunExclusiveAsync(ct => InvalidateCoreAsync(key, ct), cancellationToken).ConfigureAwait(false);
        if (changed)
        {
            OnChanged(null, null);
        }
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

    public Task<IReadOnlyList<ChapterCacheStatus>> GetCurrentConfigurationStatusesAsync(
        IReadOnlyCollection<CurrentCacheChapterQuery> chapters,
        SynthesisProfileFingerprint synthesisProfile,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(chapters);
        ArgumentNullException.ThrowIfNull(synthesisProfile);
        return RunExclusiveAsync(
            ct => _index.GetCurrentConfigurationStatusesAsync(chapters, synthesisProfile, ct),
            cancellationToken);
    }

    public Task<IReadOnlySet<AudioCacheKey>> GetValidEntriesAsync(
        IReadOnlyCollection<AudioCacheKey> keys,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var frozenKeys = keys.Distinct().ToArray();
        return GetValidEntriesCoreAsync(frozenKeys, cancellationToken);
    }

    internal Task<AudioCacheExportLeaseAcquisition> AcquireExportLeaseAsync(
        IReadOnlyList<AudioCacheKey> orderedKeys,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(orderedKeys);
        return RunExclusiveAsync(
            ct => AcquireExportLeaseCoreAsync(orderedKeys, ct),
            cancellationToken);
    }

    public async Task<AudioCacheStoreCleanupResult> ClearChapterAsync(
        string bookId,
        int chapterIndex,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookId);
        var changed = false;
        try
        {
            return await RunExclusiveAsync(
                ct => ClearEntriesCoreAsync(bookId, chapterIndex, () => changed = true, ct),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (changed)
            {
                OnChanged(bookId, chapterIndex);
            }
        }
    }

    public async Task<AudioCacheStoreCleanupResult> ClearChaptersAsync(
        string bookId,
        IReadOnlyCollection<int> chapterIndices,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookId);
        ArgumentNullException.ThrowIfNull(chapterIndices);
        var normalizedIndices = chapterIndices
            .Distinct()
            .Order()
            .ToArray();
        if (normalizedIndices.Length == 0)
        {
            throw new ArgumentException("At least one chapter must be selected.", nameof(chapterIndices));
        }

        if (normalizedIndices[0] < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chapterIndices));
        }

        var changed = false;
        try
        {
            return await RunExclusiveAsync(
                ct => ClearChaptersCoreAsync(
                    bookId,
                    normalizedIndices,
                    () => changed = true,
                    ct),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (changed)
            {
                OnChanged(bookId, null);
            }
        }
    }

    public async Task<AudioCacheStoreCleanupResult> ClearBookAsync(
        string bookId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookId);
        var changed = false;
        try
        {
            return await RunExclusiveAsync(
                ct => ClearEntriesCoreAsync(
                    bookId,
                    chapterIndex: null,
                    () => changed = true,
                    ct),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (changed)
            {
                OnChanged(bookId, null);
            }
        }
    }

    public async Task<AudioCacheStoreCleanupResult> ClearAllAsync(CancellationToken cancellationToken)
    {
        var changed = false;
        try
        {
            return await RunExclusiveAsync(
                ct => ClearAllCoreAsync(() => changed = true, ct),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (changed)
            {
                OnChanged(null, null);
            }
        }
    }

    public async Task RunMaintenanceAsync(CancellationToken cancellationToken)
    {
        var changed = false;
        try
        {
            await RunExclusiveAsync(
                ct => _maintenance.RunAsync(ct, () => changed = true),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (changed)
            {
                OnChanged(null, null);
            }
        }
    }

    private async Task<CacheLookupResult> TryGetCoreAsync(
        AudioCacheKey key,
        CancellationToken cancellationToken)
    {
        var entry = await _index.FindAsync(key, cancellationToken).ConfigureAwait(false);
        if (entry is null)
        {
            return new CacheLookupResult(null, false);
        }

        var filePath = _fileStore.ResolveIndexedPath(entry.FilePath);
        if (!_fileStore.Exists(filePath))
        {
            await _index.RemoveAsync(entry.CacheKey, cancellationToken).ConfigureAwait(false);
            return new CacheLookupResult(null, true);
        }

        await _index.TouchAsync(
            entry.CacheKey,
            _fileStore.GetStorageKey(filePath),
            cancellationToken).ConfigureAwait(false);
        return new CacheLookupResult(new AudioCacheEntry(key, filePath), false);
    }

    private async Task<IReadOnlySet<AudioCacheKey>> GetValidEntriesCoreAsync(
        IReadOnlyCollection<AudioCacheKey> keys,
        CancellationToken cancellationToken)
    {
        var entries = await _index.FindManyAsync(keys, cancellationToken).ConfigureAwait(false);
        var validKeys = new HashSet<AudioCacheKey>();
        foreach (var key in keys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!entries.TryGetValue(key.Value, out var entry))
            {
                continue;
            }

            var filePath = TryResolveValidFilePath(entry, cancellationToken);
            if (filePath is null)
            {
                continue;
            }

            validKeys.Add(key);
        }

        return validKeys;
    }

    private string? TryResolveValidFilePath(
        AudioCacheIndexEntry entry,
        CancellationToken cancellationToken)
    {
        try
        {
            var filePath = _fileStore.ResolveIndexedPath(entry.FilePath);
            if (!_fileStore.Exists(filePath) || !_audioProbe.CanDecode(filePath))
            {
                return null;
            }

            cancellationToken.ThrowIfCancellationRequested();
            return filePath;
        }
        catch (Exception exception) when (IsInvalidCacheEntry(exception))
        {
            // Malformed or inaccessible indexed paths are invalid inputs for read-only cache queries.
            return null;
        }
    }

    private async Task<AudioCacheExportLeaseAcquisition> AcquireExportLeaseCoreAsync(
        IReadOnlyList<AudioCacheKey> orderedKeys,
        CancellationToken cancellationToken)
    {
        var pathsByKey = new Dictionary<AudioCacheKey, string>();
        foreach (var key in orderedKeys.Distinct())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var filePath = await TryResolveValidFilePathAsync(key, cancellationToken).ConfigureAwait(false);
            if (filePath is null)
            {
                return AudioCacheExportLeaseAcquisition.Incomplete(key);
            }

            pathsByKey.Add(key, filePath);
        }

        var protections = new List<IDisposable>(pathsByKey.Count);
        try
        {
            foreach (var filePath in pathsByKey.Values)
            {
                cancellationToken.ThrowIfCancellationRequested();
                protections.Add(_protectionRegistry.Protect(filePath));
            }

            return AudioCacheExportLeaseAcquisition.Complete(
                new AudioCacheExportLease(
                    orderedKeys.Select(key => pathsByKey[key]).ToArray(),
                    protections));
        }
        catch
        {
            foreach (var protection in protections)
            {
                protection.Dispose();
            }

            throw;
        }
    }

    private async Task<string?> TryResolveValidFilePathAsync(
        AudioCacheKey key,
        CancellationToken cancellationToken)
    {
        var entry = await _index.FindAsync(key, cancellationToken).ConfigureAwait(false);
        if (entry is null)
        {
            return null;
        }

        try
        {
            var filePath = _fileStore.ResolveIndexedPath(entry.FilePath);
            if (!_fileStore.Exists(filePath) || !_audioProbe.CanDecode(filePath))
            {
                return null;
            }

            cancellationToken.ThrowIfCancellationRequested();
            return filePath;
        }
        catch (Exception exception) when (IsInvalidCacheEntry(exception))
        {
            // Malformed or inaccessible indexed paths are invalid inputs for read-only cache queries.
            return null;
        }
    }

    private static bool IsInvalidCacheEntry(Exception exception) =>
        exception is InvalidDataException or
            IOException or
            UnauthorizedAccessException or
            NotSupportedException;

    private async Task<AudioCacheEntry> StoreCoreAsync(
        AudioCacheWriteRequest request,
        Action storeChanged,
        Action maintenanceChanged,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_audioProbe.CanDecode(request.SourceFilePath))
        {
            throw new InvalidDataException("源音频无法通过可播放性校验。");
        }

        var destinationPath = _fileStore.GetDestinationPath(request);
        using var finalProtection = _protectionRegistry.Protect(destinationPath);
        var file = await _fileStore.StoreAsync(request, cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_audioProbe.CanDecode(file.FilePath))
            {
                throw new InvalidDataException("缓存音频无法通过可播放性校验。");
            }

            await _index.UpsertAsync(
                request,
                file.StorageKey,
                file.FileSize,
                cancellationToken).ConfigureAwait(false);
            storeChanged();
        }
        catch
        {
            if (file.CreatedNew)
            {
                _fileStore.TryDeleteFile(file.FilePath);
            }

            throw;
        }

        await _maintenance
            .EnforceLimitAsync(cancellationToken, maintenanceChanged)
            .ConfigureAwait(false);
        return new AudioCacheEntry(request.Key, file.FilePath);
    }

    private async Task<bool> InvalidateCoreAsync(AudioCacheKey key, CancellationToken cancellationToken)
    {
        var entry = await _index.FindAsync(key, cancellationToken).ConfigureAwait(false);
        if (entry is null)
        {
            return false;
        }

        var filePath = _fileStore.ResolveIndexedPath(entry.FilePath);
        await _index.RemoveAsync(entry.CacheKey, cancellationToken).ConfigureAwait(false);
        _fileStore.TryDeleteFile(filePath);
        return true;
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
        Action cacheChanged,
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
                cacheChanged();
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

    private async Task<AudioCacheStoreCleanupResult> ClearChaptersCoreAsync(
        string bookId,
        IReadOnlyCollection<int> chapterIndices,
        Action cacheChanged,
        CancellationToken cancellationToken)
    {
        var deletedBytes = 0L;
        var deletedEntryCount = 0;
        var protectedEntryCount = 0;
        var failedEntryCount = 0;

        foreach (var chapterIndex in chapterIndices)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await ClearEntriesCoreAsync(
                bookId,
                chapterIndex,
                cacheChanged,
                cancellationToken).ConfigureAwait(false);
            deletedBytes += result.DeletedBytes;
            deletedEntryCount += result.DeletedEntryCount;
            protectedEntryCount += result.ProtectedEntryCount;
            failedEntryCount += result.FailedEntryCount;
        }

        return new AudioCacheStoreCleanupResult(
            deletedBytes,
            deletedEntryCount,
            protectedEntryCount,
            failedEntryCount);
    }

    private async Task<AudioCacheStoreCleanupResult> ClearAllCoreAsync(
        Action cacheChanged,
        CancellationToken cancellationToken)
    {
        var result = await ClearEntriesCoreAsync(
            null,
            null,
            cacheChanged,
            cancellationToken).ConfigureAwait(false);
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

    private void OnChanged(string? bookId, int? chapterIndex)
    {
        Changed?.Invoke(this, new CacheChangedEventArgs(bookId, chapterIndex));
    }
}

internal sealed record CacheLookupResult(
    AudioCacheEntry? Entry,
    bool RemovedStaleEntry);

internal sealed record AudioCacheExportLeaseAcquisition(
    AudioCacheExportLease? Lease,
    AudioCacheKey? MissingKey)
{
    public static AudioCacheExportLeaseAcquisition Complete(AudioCacheExportLease lease) =>
        new(lease, null);

    public static AudioCacheExportLeaseAcquisition Incomplete(AudioCacheKey missingKey) =>
        new(null, missingKey);
}

internal sealed class AudioCacheExportLease : IDisposable
{
    private readonly IReadOnlyList<IDisposable> _protections;
    private bool _disposed;

    public AudioCacheExportLease(
        IReadOnlyList<string> orderedFilePaths,
        IReadOnlyList<IDisposable> protections)
    {
        OrderedFilePaths = orderedFilePaths;
        _protections = protections;
    }

    public IReadOnlyList<string> OrderedFilePaths { get; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        for (var index = _protections.Count - 1; index >= 0; index--)
        {
            _protections[index].Dispose();
        }

        _disposed = true;
    }
}
