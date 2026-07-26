using Microsoft.Data.Sqlite;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Books.TextProcessing;
using NovelSpeaker.Domain.Books;
using NovelSpeaker.Infrastructure.FileSystem;
using NovelSpeaker.Infrastructure.Persistence;
using Xunit;

namespace NovelSpeaker.Infrastructure.IntegrationTests.Books;

public sealed class RegexReplacementRuleRepositoryTests
{
    [Fact]
    public async Task GetAllAsync_skips_malformed_rows_without_losing_valid_rules()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            var directories = new LocalAppDataDirectoryProvider(root);
            var factory = new SqliteConnectionFactory(directories);
            await directories.EnsureCreatedAsync(CancellationToken.None);
            await new SqliteMigrationRunner(factory).InitializeAsync(CancellationToken.None);
            var repository = new RegexReplacementRuleRepository(factory, TimeProvider.System);
            var validRule = new RegexReplacementRule(
                Guid.NewGuid(),
                "有效规则",
                true,
                20,
                "a",
                "b",
                RegexReplacementScope.Both,
                DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch);
            await repository.SaveAsync(validRule, CancellationToken.None);

            await using (var connection = await factory.OpenConnectionAsync(CancellationToken.None))
            {
                var command = connection.CreateCommand();
                command.CommandText =
                    """
                    INSERT INTO RegexReplacementRules
                        (Id, Name, IsEnabled, SortOrder, Pattern, Replacement, Scope, CreatedAt, UpdatedAt)
                    VALUES
                        ($id, '损坏规则', 1, 10, '[', '', 'Unknown', 'not-a-date', 'not-a-date');
                    """;
                command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
                await command.ExecuteNonQueryAsync(CancellationToken.None);
            }

            var rules = await repository.GetAllAsync(CancellationToken.None);
            var pipeline = new RegexReplacementPipeline(
                repository,
                new RegexReplacementRuleErrorStore());
            var result = await pipeline.ApplyAsync(
                [new SpeechSegment(0, 0, 1, "a", "a")],
                CancellationToken.None);

            Assert.Equal(validRule.Id, Assert.Single(rules).Id);
            Assert.Equal("b", Assert.Single(result.Segments).SpeechText);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                SqliteConnection.ClearAllPools();
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
