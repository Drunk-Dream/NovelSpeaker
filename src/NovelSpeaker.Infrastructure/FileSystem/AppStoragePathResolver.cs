using NovelSpeaker.Application.Abstractions;

namespace NovelSpeaker.Infrastructure.FileSystem;

/// <summary>
/// Resolves storage paths under one canonical application root and rejects reparse-point traversal.
/// </summary>
public sealed class AppStoragePathResolver : IAppStoragePathResolver
{
    private readonly string _rootPath;
    private readonly string _rootPrefix;
    private readonly StringComparison _pathComparison;

    public AppStoragePathResolver(IAppDataDirectoryProvider directories)
    {
        _rootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directories.RootDirectoryPath));
        _rootPrefix = _rootPath + Path.DirectorySeparatorChar;
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
        EnsureNoReparsePoint(candidate);
        return candidate;
    }

    public string GetStorageKey(string appOwnedPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appOwnedPath);

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

    private void EnsureNoReparsePoint(string candidate)
    {
        var relative = Path.GetRelativePath(_rootPath, candidate);
        var current = _rootPath;
        foreach (var segment in relative.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (!File.Exists(current) && !Directory.Exists(current))
            {
                continue;
            }

            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException("持久化路径不能经过 reparse point。");
            }
        }
    }
}
