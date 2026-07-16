using Microsoft.Data.Sqlite;
using NovelSpeaker.Infrastructure.FileSystem;
using NovelSpeaker.Infrastructure.Persistence;
using Xunit;

namespace NovelSpeaker.UnitTests.Persistence;

public sealed class SqliteConnectionFactoryTests
{
    [Fact]
    public async Task OpenConnectionAsync_configures_foreign_keys_busy_timeout_and_default_timeout_on_every_connection()
    {
        var factory = await CreateFactoryAsync();

        await using var first = await factory.OpenConnectionAsync(CancellationToken.None);
        await using var second = await factory.OpenConnectionAsync(CancellationToken.None);

        Assert.Equal(5, first.DefaultTimeout);
        Assert.Equal(5, second.DefaultTimeout);
        Assert.Equal(1L, await ExecutePragmaAsync(first, "foreign_keys"));
        Assert.Equal(1L, await ExecutePragmaAsync(second, "foreign_keys"));
        Assert.Equal(5000L, await ExecutePragmaAsync(first, "busy_timeout"));
        Assert.Equal(5000L, await ExecutePragmaAsync(second, "busy_timeout"));
    }

    [Fact]
    public async Task OpenConnectionAsync_honors_pre_cancelled_token()
    {
        var factory = await CreateFactoryAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await factory.OpenConnectionAsync(cancellation.Token));
    }

    private static async Task<SqliteConnectionFactory> CreateFactoryAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var directories = new LocalAppDataDirectoryProvider(root);
        await directories.EnsureCreatedAsync(CancellationToken.None);
        return new SqliteConnectionFactory(directories);
    }

    private static async Task<long> ExecutePragmaAsync(SqliteConnection connection, string pragma)
    {
        var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA {pragma};";
        return Convert.ToInt64(await command.ExecuteScalarAsync(CancellationToken.None));
    }
}
