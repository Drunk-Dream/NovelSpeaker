namespace NovelSpeaker.Infrastructure.FileSystem;

/// <summary>
/// Checks reparse points within an explicitly trusted path boundary.
/// </summary>
internal static class ReparsePointPathGuard
{
    public static bool ContainsReparsePoint(string path, string trustedRoot, bool includeRoot = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(trustedRoot);

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(trustedRoot));
        var candidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (!IsSamePathOrDescendant(candidate, root, comparison))
        {
            throw new ArgumentException("待检查路径必须位于可信根目录内部。", nameof(path));
        }

        if (includeRoot && IsReparsePoint(root))
        {
            return true;
        }

        if (string.Equals(candidate, root, comparison))
        {
            return false;
        }

        var current = root;
        foreach (var segment in Path.GetRelativePath(root, candidate).Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (IsReparsePoint(current))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsReparsePoint(string path) =>
        (File.Exists(path) || Directory.Exists(path)) &&
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static bool IsSamePathOrDescendant(
        string path,
        string root,
        StringComparison comparison)
    {
        if (string.Equals(path, root, comparison))
        {
            return true;
        }

        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar) ||
                                root.EndsWith(Path.AltDirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        return path.StartsWith(rootWithSeparator, comparison);
    }
}
