using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Domain.Common;

namespace NovelSpeaker.Infrastructure.FileSystem;

/// <summary>
/// Creates and exposes the app-owned directory structure under LocalAppData.
/// </summary>
public sealed class LocalAppDataDirectoryProvider : IAppDataDirectoryProvider
{
    public LocalAppDataDirectoryProvider()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppInfo.ProductName))
    {
    }

    public LocalAppDataDirectoryProvider(string rootDirectoryPath)
    {
        RootDirectoryPath = rootDirectoryPath;
        DatabasePath = Path.Combine(rootDirectoryPath, "app.db");
        SettingsPath = Path.Combine(rootDirectoryPath, "settings.json");
        BooksDirectoryPath = Path.Combine(rootDirectoryPath, "Books");
        CacheDirectoryPath = Path.Combine(rootDirectoryPath, "Cache");
        LogsDirectoryPath = Path.Combine(rootDirectoryPath, "Logs");
        OperationsDirectoryPath = Path.Combine(rootDirectoryPath, "Operations");
    }

    public string RootDirectoryPath { get; }
    public string DatabasePath { get; }
    public string SettingsPath { get; }
    public string BooksDirectoryPath { get; }
    public string CacheDirectoryPath { get; }
    public string LogsDirectoryPath { get; }
    public string OperationsDirectoryPath { get; }

    public Task EnsureCreatedAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Directory.CreateDirectory(RootDirectoryPath);
        Directory.CreateDirectory(BooksDirectoryPath);
        Directory.CreateDirectory(CacheDirectoryPath);
        Directory.CreateDirectory(LogsDirectoryPath);
        Directory.CreateDirectory(OperationsDirectoryPath);

        return Task.CompletedTask;
    }
}
