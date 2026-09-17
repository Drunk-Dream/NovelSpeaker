using NovelSpeaker.Application.Abstractions;

namespace NovelSpeaker.Infrastructure.FileSystem;

/// <summary>
/// Exposes the stable application-owned directory layout under a selected data root.
/// </summary>
public sealed class AppDataDirectoryProvider : IAppDataDirectoryProvider
{
    private readonly AppDataStorageTrustBoundary _trustBoundary;

    public AppDataDirectoryProvider(string rootDirectoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectoryPath);

        RootDirectoryPath = Path.GetFullPath(rootDirectoryPath);
        _trustBoundary = new AppDataStorageTrustBoundary(RootDirectoryPath);
    }

    public string RootDirectoryPath { get; }
    public string DatabasePath => ResolveManagedPath("app.db");
    public string SettingsPath => ResolveManagedPath("settings.json");
    public string BooksDirectoryPath => ResolveManagedPath("Books");
    public string CacheDirectoryPath => ResolveManagedPath("Cache");
    public string LogsDirectoryPath => ResolveManagedPath("Logs");
    public string OperationsDirectoryPath => ResolveManagedPath("Operations");
    public string DiagnosticsDirectoryPath => ResolveManagedPath("Diagnostics");
    public string ActiveDiagnosticSessionMarkerPath => ResolveManagedPath(Path.Combine("Diagnostics", "active-session.marker"));

    public Task EnsureCreatedAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Directory.CreateDirectory(RootDirectoryPath);
        foreach (var relativeDirectoryPath in new[]
                 {
                     "Books",
                     "Cache",
                     "Logs",
                     "Operations",
                     "Diagnostics"
                 })
        {
            var directoryPath = ResolveManagedPath(relativeDirectoryPath);
            Directory.CreateDirectory(directoryPath);
            _ = ResolveManagedPath(relativeDirectoryPath);
        }

        _ = DatabasePath;
        _ = SettingsPath;
        _ = ActiveDiagnosticSessionMarkerPath;

        return Task.CompletedTask;
    }

    private string ResolveManagedPath(string relativePath) =>
        _trustBoundary.ResolvePath(Path.Combine(RootDirectoryPath, relativePath));
}
