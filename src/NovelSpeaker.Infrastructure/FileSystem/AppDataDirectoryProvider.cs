using NovelSpeaker.Application.Abstractions;

namespace NovelSpeaker.Infrastructure.FileSystem;

/// <summary>
/// Exposes the stable application-owned directory layout under a selected data root.
/// </summary>
public sealed class AppDataDirectoryProvider : IAppDataDirectoryProvider
{
    public AppDataDirectoryProvider(string rootDirectoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectoryPath);

        RootDirectoryPath = Path.GetFullPath(rootDirectoryPath);
        DatabasePath = Path.Combine(RootDirectoryPath, "app.db");
        SettingsPath = Path.Combine(RootDirectoryPath, "settings.json");
        BooksDirectoryPath = Path.Combine(RootDirectoryPath, "Books");
        CacheDirectoryPath = Path.Combine(RootDirectoryPath, "Cache");
        LogsDirectoryPath = Path.Combine(RootDirectoryPath, "Logs");
        OperationsDirectoryPath = Path.Combine(RootDirectoryPath, "Operations");
        DiagnosticsDirectoryPath = Path.Combine(RootDirectoryPath, "Diagnostics");
        ActiveDiagnosticSessionMarkerPath = Path.Combine(DiagnosticsDirectoryPath, "active-session.marker");
    }

    public string RootDirectoryPath { get; }
    public string DatabasePath { get; }
    public string SettingsPath { get; }
    public string BooksDirectoryPath { get; }
    public string CacheDirectoryPath { get; }
    public string LogsDirectoryPath { get; }
    public string OperationsDirectoryPath { get; }
    public string DiagnosticsDirectoryPath { get; }
    public string ActiveDiagnosticSessionMarkerPath { get; }

    public Task EnsureCreatedAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Directory.CreateDirectory(RootDirectoryPath);
        Directory.CreateDirectory(BooksDirectoryPath);
        Directory.CreateDirectory(CacheDirectoryPath);
        Directory.CreateDirectory(LogsDirectoryPath);
        Directory.CreateDirectory(OperationsDirectoryPath);
        if (ContainsReparsePointBelowRoot(DiagnosticsDirectoryPath, RootDirectoryPath))
        {
            throw new IOException("诊断目录或数据根目录内部的父路径包含不受信任的 reparse point。");
        }

        Directory.CreateDirectory(DiagnosticsDirectoryPath);

        return Task.CompletedTask;
    }

    private static bool ContainsReparsePointBelowRoot(string path, string rootDirectoryPath)
    {
        var root = Path.GetFullPath(rootDirectoryPath);
        var current = Path.GetFullPath(path);
        var comparison = GetPathComparison();

        if (!IsSamePathOrDescendant(current, root, comparison))
        {
            throw new ArgumentException("待检查路径必须位于应用数据根目录内部。", nameof(path));
        }

        while (!string.Equals(current, root, comparison))
        {
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                return true;
            }

            var parent = Directory.GetParent(current)?.FullName;
            if (parent is null)
            {
                throw new IOException("无法在应用数据根目录边界内解析诊断目录路径。");
            }

            current = Path.GetFullPath(parent);
        }

        return false;
    }

    private static bool IsSamePathOrDescendant(
        string path,
        string rootDirectoryPath,
        StringComparison comparison)
    {
        if (string.Equals(path, rootDirectoryPath, comparison))
        {
            return true;
        }

        var rootWithSeparator = rootDirectoryPath.EndsWith(Path.DirectorySeparatorChar) ||
                                rootDirectoryPath.EndsWith(Path.AltDirectorySeparatorChar)
            ? rootDirectoryPath
            : rootDirectoryPath + Path.DirectorySeparatorChar;

        return path.StartsWith(rootWithSeparator, comparison);
    }

    private static StringComparison GetPathComparison() =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
