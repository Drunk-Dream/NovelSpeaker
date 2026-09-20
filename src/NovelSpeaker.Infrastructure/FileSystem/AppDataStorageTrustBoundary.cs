namespace NovelSpeaker.Infrastructure.FileSystem;

/// <summary>
/// Resolves application-owned paths within the logical data root and rejects links below that root.
/// The root itself is the trust anchor, so links in it or its ancestors remain supported.
/// </summary>
internal sealed class AppDataStorageTrustBoundary
{
    private readonly string _rootPath;
    private readonly string _rootPrefix;
    private readonly StringComparison _pathComparison;

    public AppDataStorageTrustBoundary(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

        _rootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        _rootPrefix = _rootPath.EndsWith(Path.DirectorySeparatorChar) ||
                      _rootPath.EndsWith(Path.AltDirectorySeparatorChar)
            ? _rootPath
            : _rootPath + Path.DirectorySeparatorChar;
        _pathComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
    }

    public string ResolvePath(string storageKeyOrLegacyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageKeyOrLegacyPath);

        var candidate = Path.IsPathFullyQualified(storageKeyOrLegacyPath)
            ? Path.GetFullPath(storageKeyOrLegacyPath)
            : Path.GetFullPath(Path.Combine(_rootPath, NormalizeStorageKey(storageKeyOrLegacyPath)));

        EnsureContained(candidate);
        EnsureNoUntrustedReparsePoint(candidate);
        return candidate;
    }

    public string GetStorageKey(string appOwnedPath)
    {
        var resolved = ResolvePath(appOwnedPath);
        if (string.Equals(resolved, _rootPath, _pathComparison))
        {
            throw new InvalidDataException("应用数据根目录不能作为存储文件键。");
        }

        return Path.GetRelativePath(_rootPath, resolved)
            .Replace(Path.DirectorySeparatorChar, '/');
    }

    private static string NormalizeStorageKey(string storageKey)
    {
        if (storageKey.IndexOf('\0') >= 0)
        {
            throw new InvalidDataException("存储键包含无效字符。");
        }

        return storageKey.Replace('/', Path.DirectorySeparatorChar)
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
    }

    private void EnsureContained(string candidate)
    {
        if (!string.Equals(candidate, _rootPath, _pathComparison) &&
            !candidate.StartsWith(_rootPrefix, _pathComparison))
        {
            throw new InvalidDataException("持久化路径超出应用数据目录。");
        }
    }

    private void EnsureNoUntrustedReparsePoint(string candidate)
    {
        if (string.Equals(candidate, _rootPath, _pathComparison))
        {
            return;
        }

        var current = _rootPath;
        foreach (var segment in Path.GetRelativePath(_rootPath, candidate).Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (IsReparsePoint(current))
            {
                throw new InvalidDataException("持久化路径不能经过数据根内部的 reparse point。");
            }
        }
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
    }
}
