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

        Assert.Equal("文件名", Assert.Single(await fileNames.GetAllAsync(CancellationToken.None)).Name);
        Assert.Equal("正文", Assert.Single(await headers.GetAllAsync(CancellationToken.None)).Name);

        await fileNames.DeleteAsync("shared", CancellationToken.None);
        Assert.Empty(await fileNames.GetAllAsync(CancellationToken.None));
        Assert.Single(await headers.GetAllAsync(CancellationToken.None));
    }
}
