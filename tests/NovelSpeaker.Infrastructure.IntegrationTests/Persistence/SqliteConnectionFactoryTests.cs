using Microsoft.Data.Sqlite;
using NovelSpeaker.Infrastructure.FileSystem;
using NovelSpeaker.Infrastructure.Persistence;
using Xunit;

namespace NovelSpeaker.Infrastructure.IntegrationTests.Persistence;

public sealed class SqliteConnectionFactoryTests
{
    [Fact]
    public async Task OpenConnectionAsync_enables_foreign_keys_on_every_connection()
    {
        var factory = await CreateFactoryAsync();

        await using var first = await factory.OpenConnectionAsync(CancellationToken.None);
        await using var second = await factory.OpenConnectionAsync(CancellationToken.None);

        Assert.Equal(1L, await ExecutePragmaAsync(first, "foreign_keys"));
        Assert.Equal(1L, await ExecutePragmaAsync(second, "foreign_keys"));
    }

    private static async Task<SqliteConnectionFactory> CreateFactoryAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var directories = new AppDataDirectoryProvider(root);
        await directories.EnsureCreatedAsync(CancellationToken.None);
        return new SqliteConnectionFactory(directories);
    }

    private static async Task<long> ExecutePragmaAsync(SqliteConnection connection, string pragma)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA {pragma};";
        return Convert.ToInt64(await command.ExecuteScalarAsync(CancellationToken.None));
    }
}
