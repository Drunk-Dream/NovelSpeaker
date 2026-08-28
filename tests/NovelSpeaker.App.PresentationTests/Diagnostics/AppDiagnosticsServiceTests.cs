using Microsoft.Data.Sqlite;
using NovelSpeaker.App.Features.Diagnostics;
using NovelSpeaker.App.Shared.Presentation.Platform;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Domain.Settings;
using NovelSpeaker.Infrastructure.FileSystem;
using NovelSpeaker.Infrastructure.Persistence;
using Xunit;

namespace NovelSpeaker.App.PresentationTests.Diagnostics;

public sealed class AppDiagnosticsServiceTests
{
    [Fact]
    public async Task GetSnapshotAsync_reads_schema_version_and_paths()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var directories = new AppDataDirectoryProvider(root);
        await directories.EnsureCreatedAsync(CancellationToken.None);

        await using (var connection = new SqliteConnection($"Data Source={directories.DatabasePath}"))
        {
            await connection.OpenAsync(CancellationToken.None);
            var command = connection.CreateCommand();
            command.CommandText =
                """
                CREATE TABLE SchemaVersion (
                    Version INTEGER NOT NULL PRIMARY KEY
                );
                INSERT INTO SchemaVersion (Version) VALUES (4);
                """;
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }

        var service = new AppDiagnosticsService(
            directories,
            new SqliteDatabaseSchemaVersionProvider(new SqliteConnectionFactory(directories)),
            new FakeAppSettingsService(AppSettings.Default with { Theme = "Dark", LogLevel = "Warning" }),
            new FakeLauncher());

        var snapshot = await service.GetSnapshotAsync(CancellationToken.None);

        Assert.Equal("NovelSpeaker", snapshot.AppName);
        Assert.Equal(4, snapshot.DatabaseSchemaVersion);
        Assert.Equal(root, snapshot.AppDataDirectoryPath);
        Assert.Equal(Path.Combine(root, "Logs"), snapshot.LogsDirectoryPath);

        var summary = await service.GetRedactedSummaryAsync(CancellationToken.None);
        Assert.Contains("主题：Dark", summary);
        Assert.Contains("日志级别：Warning", summary);
        Assert.DoesNotContain("Authorization", summary, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakeAppSettingsService : IAppSettingsService
    {
        private readonly AppSettings _settings;

        public FakeAppSettingsService(AppSettings settings) => _settings = settings;

        public AppSettings Current => _settings;
        public event EventHandler<AppSettingsChangedEventArgs>? Changed { add { } remove { } }

        public Task<AppSettings> UpdateAsync(AppSettingsUpdate update, CancellationToken cancellationToken) => Task.FromResult(_settings);
    }

    private sealed class FakeLauncher : IPresentationLauncher
    {
        public Task OpenAsync(string path, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
