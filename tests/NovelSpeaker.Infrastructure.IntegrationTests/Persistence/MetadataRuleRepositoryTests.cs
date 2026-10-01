using NovelSpeaker.Domain.Books;
using NovelSpeaker.Infrastructure.FileSystem;
using NovelSpeaker.Infrastructure.Persistence;
using Xunit;

namespace NovelSpeaker.Infrastructure.IntegrationTests.Persistence;

public sealed class MetadataRuleRepositoryTests
{
    [Fact]
    public async Task Repositories_keep_filename_and_header_rules_independent()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var directories = new AppDataDirectoryProvider(root);
        await directories.EnsureCreatedAsync(CancellationToken.None);
        var factory = new SqliteConnectionFactory(directories);
        await new SqliteMigrationRunner(factory).InitializeAsync(CancellationToken.None);
        var fileNames = new FileNameMetadataRuleRepository(factory);
        var headers = new TextHeaderMetadataRuleRepository(factory);
        var now = DateTimeOffset.UtcNow;

        await fileNames.SaveAsync(new FileNameMetadataRule("shared", "文件名", "(?<name>.+)", 20, true, now, now), CancellationToken.None);
        await headers.SaveAsync(new TextHeaderMetadataRule("shared", "正文", "(?<author>.+)", 10, false, now, now), CancellationToken.None);

        Assert.Equal("文件名", (await fileNames.GetAllAsync(CancellationToken.None)).Single(rule => rule.Id == "shared").Name);
        Assert.Equal("正文", (await headers.GetAllAsync(CancellationToken.None)).Single(rule => rule.Id == "shared").Name);

        await fileNames.DeleteAsync("shared", CancellationToken.None);
        Assert.DoesNotContain(await fileNames.GetAllAsync(CancellationToken.None), rule => rule.Id == "shared");
        Assert.Contains(await headers.GetAllAsync(CancellationToken.None), rule => rule.Id == "shared");
    }

    [Fact]
    public async Task Default_rules_are_seeded_once_and_user_deletions_stay_deleted()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var directories = new AppDataDirectoryProvider(root);
        await directories.EnsureCreatedAsync(CancellationToken.None);
        var factory = new SqliteConnectionFactory(directories);
        await new SqliteMigrationRunner(factory).InitializeAsync(CancellationToken.None);
        var fileNames = new FileNameMetadataRuleRepository(factory);
        var headers = new TextHeaderMetadataRuleRepository(factory);

        var filenameDefault = Assert.Single(await fileNames.GetAllAsync(CancellationToken.None));
        Assert.Matches(filenameDefault.Pattern, "书名 作者：作者");
        Assert.Equal(3, (await headers.GetAllAsync(CancellationToken.None)).Count);

        await fileNames.DeleteAsync("default:filename-name-author", CancellationToken.None);
        await new SqliteMigrationRunner(factory).InitializeAsync(CancellationToken.None);
        Assert.Empty(await fileNames.GetAllAsync(CancellationToken.None));
    }
}
