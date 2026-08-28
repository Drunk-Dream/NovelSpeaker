using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Books.TextProcessing;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Playback.Cache;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Domain.Books;
using NovelSpeaker.Infrastructure.Books.FileStorage;
using NovelSpeaker.Infrastructure.FileSystem;
using NovelSpeaker.Infrastructure.Persistence;
using NovelSpeaker.Infrastructure.Persistence.Books;
using NovelSpeaker.Infrastructure.Persistence.Playback;
using Xunit;

namespace NovelSpeaker.Infrastructure.IntegrationTests;

public sealed class ChapterSpeechPlanPlaybackIntegrationTests
{
    [Fact]
    public async Task Playback_content_service_reuses_the_shared_plan_builder_and_persists_one_current_plan()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var directories = new AppDataDirectoryProvider(root);
        var factory = new SqliteConnectionFactory(directories);
        var runner = new SqliteMigrationRunner(factory);
        var chapterRuleRepository = new ChapterRuleRepository(factory);
        await new StartupDatabaseInitializer(
                directories,
                runner,
                new DefaultChapterRuleSeeder(chapterRuleRepository))
            .InitializeAsync(CancellationToken.None);

        var contentPath = Path.Combine(directories.BooksDirectoryPath, "book-1", "content.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(contentPath)!);
        await File.WriteAllTextAsync(contentPath, "第一段。\n第二段。", CancellationToken.None);
        await using (var connection = await factory.OpenConnectionAsync(CancellationToken.None))
        {
            var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO Books
                    (Id, Title, OriginalFileName, StoredFilePath, SourceHash, Encoding, ImportedAt, UpdatedAt)
                VALUES
                    ('book-1', '书', 'book.txt', 'Books/book-1/content.txt', 'playback-plan', 'utf-8', '2026-01-01T00:00:00.0000000+00:00', '2026-01-01T00:00:00.0000000+00:00');
                INSERT INTO Chapters (Id, BookId, ChapterIndex, SortOrder, Title, StartOffset, Length)
                VALUES ('chapter-1', 'book-1', 0, 0, '第一章', 0, 9);
                """;
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }

        var regexRepository = new EmptyRegexReplacementRuleRepository();
        var planService = new ChapterSpeechPlanService(
            new TextSegmenter(),
            new PassthroughRegexReplacementPipeline(),
            regexRepository,
            new SqliteChapterSpeechPlanStore(factory),
            TimeProvider.System);
        var service = new BookPlaybackContentService(
            new SqliteBookPlaybackMetadataQuery(factory),
            new BookContentReader(new AppStoragePathResolver(directories)),
            new TextSegmenter(),
            new StaticTextSegmentationOptionsProvider(TextSegmentationOptions.Default),
            new PassthroughRegexReplacementPipeline(),
            speechPlanService: planService);

        var chapter = await service.GetChapterAsync("book-1", 0, CancellationToken.None);
        var plan = await new SqliteChapterSpeechPlanStore(factory)
            .GetAsync("chapter-1", CancellationToken.None);

        Assert.NotNull(chapter);
        Assert.Equal(2, chapter!.Segments.Count);
        Assert.NotNull(plan);
        Assert.Equal(2, plan!.BodySegmentCount);
        Assert.Equal([0, 5], plan.Segments.Select(segment => segment.SourceStartOffset));
    }

    private sealed class StaticTextSegmentationOptionsProvider : ITextSegmentationOptionsProvider
    {
        private readonly TextSegmentationOptions _options;

        public StaticTextSegmentationOptionsProvider(TextSegmentationOptions options)
        {
            _options = options;
        }

        public TextSegmentationOptions GetCurrent() => _options;
    }

    private sealed class PassthroughRegexReplacementPipeline : IRegexReplacementPipeline
    {
        public Task<RegexReplacementPipelineResult> ApplyAsync(
            IReadOnlyList<SpeechSegment> sourceSegments,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new RegexReplacementPipelineResult(
                sourceSegments,
                new Dictionary<Guid, string>(),
                []));
        }
    }

    private sealed class EmptyRegexReplacementRuleRepository : IRegexReplacementRuleRepository
    {
        public Task<IReadOnlyList<RegexReplacementRule>> GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RegexReplacementRule>>([]);

        public Task SaveAsync(RegexReplacementRule rule, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task UpdateEnabledAsync(Guid ruleId, bool isEnabled, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SaveOrderAsync(IReadOnlyList<(Guid RuleId, int SortOrder)> order, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task DeleteAsync(Guid ruleId, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
