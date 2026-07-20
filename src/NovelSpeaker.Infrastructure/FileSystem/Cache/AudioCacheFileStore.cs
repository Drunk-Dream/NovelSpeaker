using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Playback.Cache;

namespace NovelSpeaker.Infrastructure.FileSystem.Cache;

/// <summary>
/// Owns cache file names, temporary files and same-volume finalization.
/// </summary>
internal sealed class AudioCacheFileStore
{
    private readonly IAppStoragePathResolver _pathResolver;
    private readonly IAudioCacheProtectionRegistry _protectionRegistry;
    private readonly string _ttsStorageKey;
    private readonly string _versionStorageKey;
    private readonly string _versionRootPath;
    private readonly StringComparison _pathComparison;

    public AudioCacheFileStore(
        IAppDataDirectoryProvider directories,
        IAppStoragePathResolver pathResolver,
        IAudioCacheProtectionRegistry protectionRegistry)
    {
        _pathResolver = pathResolver;
        _protectionRegistry = protectionRegistry;
        _pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        var cacheStorageKey = pathResolver.GetStorageKey(directories.CacheDirectoryPath);
        _ttsStorageKey = CombineStorageKey(cacheStorageKey, "Tts");
        _versionStorageKey = CombineStorageKey(_ttsStorageKey, AudioCacheKey.CurrentVersion);
        _versionRootPath = pathResolver.ResolvePath(_versionStorageKey);
    }

    public string GetDestinationPath(AudioCacheWriteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var extension = NormalizeExtension(Path.GetExtension(request.SourceFilePath));
        return ResolveCachePath(CombineStorageKey(
            _versionStorageKey,
            request.Key.Shard,
            $"{request.Key.FileNameBase}{extension}"));
    }

    public async Task<AudioCacheFile> StoreAsync(
        AudioCacheWriteRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var finalPath = GetDestinationPath(request);
        var shardDirectory = Path.GetDirectoryName(finalPath)
            ?? throw new InvalidDataException("缓存文件路径缺少所属目录。");
        var temporaryPath = ResolveCachePath(CombineStorageKey(
            _versionStorageKey,
            request.Key.Shard,
            $"{request.Key.FileNameBase}.{Guid.NewGuid():N}.tmp"));

        Directory.CreateDirectory(shardDirectory);
        using var temporaryProtection = _protectionRegistry.Protect(temporaryPath);
        try
        {
            await CopyOrMoveToTemporaryPathAsync(
                request.SourceFilePath,
                temporaryPath,
                cancellationToken).ConfigureAwait(false);

            try
            {
                if (!File.Exists(finalPath))
                {
                    File.Move(temporaryPath, finalPath);
                }
            }
            catch (IOException) when (File.Exists(finalPath))
            {
                // Another writer won the same cache key. Its complete file is valid.
            }

            var fileInfo = new FileInfo(finalPath);
            if (!fileInfo.Exists)
            {
                throw new IOException("缓存文件写入失败。");
            }

            return new AudioCacheFile(finalPath, fileInfo.Length, GetStorageKey(finalPath));
        }
        finally
        {
            TryDeleteFile(temporaryPath);
        }
    }

    public string ResolveIndexedPath(string storageKeyOrLegacyPath)
    {
        return ResolveCachePath(storageKeyOrLegacyPath);
    }

    public string GetStorageKey(string path)
    {
        return _pathResolver.GetStorageKey(ResolveCachePath(path));
    }

    public bool Exists(string path)
    {
        var resolvedPath = ResolveCachePath(path);
        return File.Exists(resolvedPath);
    }

    public bool TryDeleteFile(string path)
    {
        var resolvedPath = ResolveCachePath(path);
        try
        {
            if (!File.Exists(resolvedPath))
            {
                return true;
            }

            File.Delete(resolvedPath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void DeleteResidualTemporaryFiles(CancellationToken cancellationToken)
    {
        var ttsRootPath = _pathResolver.ResolvePath(_ttsStorageKey);
        if (!Directory.Exists(ttsRootPath))
        {
            return;
        }

        foreach (var candidate in Directory.EnumerateFiles(ttsRootPath, "*.tmp", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var filePath = ResolveCachePath(candidate);
            if (_protectionRegistry.IsProtected(filePath))
            {
                continue;
            }

            TryDeleteFile(filePath);
        }
    }

    public void DeleteOrphanCacheFiles(
        IReadOnlySet<string> knownPaths,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_versionRootPath))
        {
            return;
        }

        foreach (var candidate in Directory.EnumerateFiles(_versionRootPath, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.Equals(Path.GetExtension(candidate), ".tmp", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var filePath = ResolveCachePath(candidate);
            if (knownPaths.Contains(filePath) || _protectionRegistry.IsProtected(filePath))
            {
                continue;
            }

            TryDeleteFile(filePath);
        }
    }

    private string ResolveCachePath(string storageKeyOrLegacyPath)
    {
        var resolvedPath = _pathResolver.ResolvePath(storageKeyOrLegacyPath);
        var ttsRootPath = _pathResolver.ResolvePath(_ttsStorageKey);
        var ttsPrefix = ttsRootPath + Path.DirectorySeparatorChar;
        if (!string.Equals(resolvedPath, ttsRootPath, _pathComparison) &&
            !resolvedPath.StartsWith(ttsPrefix, _pathComparison))
        {
            throw new InvalidDataException("音频缓存路径不属于应用缓存目录。");
        }

        return resolvedPath;
    }

    private static async Task CopyOrMoveToTemporaryPathAsync(
        string sourceFilePath,
        string temporaryPath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(sourceFilePath))
        {
            throw new FileNotFoundException("未找到要缓存的源音频文件。", sourceFilePath);
        }

        try
        {
            File.Move(sourceFilePath, temporaryPath, overwrite: true);
            return;
        }
        catch (IOException)
        {
            // The source may be on another volume; fall back to a cancellable copy.
        }

        await using var source = new FileStream(
            sourceFilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var destination = new FileStream(
            temporaryPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 64 * 1024,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);
        await source.CopyToAsync(destination, 64 * 1024, cancellationToken).ConfigureAwait(false);
        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        destination.Flush(flushToDisk: true);
        await destination.DisposeAsync().ConfigureAwait(false);
        File.Delete(sourceFilePath);
    }

    private static string NormalizeExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            throw new InvalidOperationException("缓存音频文件缺少可识别的扩展名。");
        }

        return extension.StartsWith(".", StringComparison.Ordinal)
            ? extension.ToLowerInvariant()
            : $".{extension.ToLowerInvariant()}";
    }

    private static string CombineStorageKey(params string[] parts)
    {
        return string.Join(
            "/",
            parts.SelectMany(static part => part.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)));
    }
}

internal sealed record AudioCacheFile(
    string FilePath,
    long FileSize,
    string StorageKey);
