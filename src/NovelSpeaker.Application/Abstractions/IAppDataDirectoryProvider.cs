namespace NovelSpeaker.Application.Abstractions;

/// <summary>
/// Resolves and creates application-owned storage directories.
/// </summary>
public interface IAppDataDirectoryProvider
{
    string RootDirectoryPath { get; }
    string DatabasePath { get; }
    string SettingsPath { get; }
    string BooksDirectoryPath { get; }
    string CacheDirectoryPath { get; }
    string LogsDirectoryPath { get; }

    Task EnsureCreatedAsync(CancellationToken cancellationToken);
}
