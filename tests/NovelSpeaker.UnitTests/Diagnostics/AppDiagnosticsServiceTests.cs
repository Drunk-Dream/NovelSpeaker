using Microsoft.Data.Sqlite;
using NovelSpeaker.App.Diagnostics;
using NovelSpeaker.Infrastructure.FileSystem;
using NovelSpeaker.Infrastructure.Persistence;
using Xunit;

namespace NovelSpeaker.UnitTests.Diagnostics;

public sealed class AppDiagnosticsServiceTests
{
    [Fact]
    public async Task GetSnapshotAsync_reads_schema_version_and_paths()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var directories = new LocalAppDataDirectoryProvider(root);
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

        var service = new AppDiagnosticsService(directories, new SqliteConnectionFactory(directories));

        var snapshot = await service.GetSnapshotAsync(CancellationToken.None);

        Assert.Equal("NovelSpeaker", snapshot.AppName);
        Assert.Equal(4, snapshot.DatabaseSchemaVersion);
        Assert.Equal(root, snapshot.AppDataDirectoryPath);
        Assert.Equal(Path.Combine(root, "Logs"), snapshot.LogsDirectoryPath);
    }
}
