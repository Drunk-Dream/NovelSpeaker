using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Domain.Books;
using NovelSpeaker.Infrastructure.Books;
using Xunit;

namespace NovelSpeaker.UnitTests.Books;

public sealed class BookImportServiceTests
{
    [Fact]
    public async Task AnalyzeAsync_returns_duplicate_failure_when_hash_already_exists()
    {
        var analyzer = new FakeTextFileAnalyzer(new TextFileAnalysis("utf-8", "preview", "第一章 开始\n正文"));
        var normalizer = new FakeTextNormalizer("第一章 开始\n正文");
        var hasher = new FakeContentHasher("same-hash");
        var duplicates = new FakeDuplicateDetector("book-42");
        var rules = new FakeChapterRuleRepository([
            new ChapterRule("rule-1", "章节", @"^\s*第[0-9一二三四五六七八九十百千零两]+章(?:\s+.+)?\s*$", 10, true, "now", "now")
        ]);
        var splitter = new FakeChapterSplitter([new BookImportChapter(0, 0, "第一章 开始", 6, 2)]);
        var service = new BookImportService(
            analyzer,
            normalizer,
            hasher,
            duplicates,
            rules,
            splitter,
            new FakeBookFileStore(),
            new FakeBookImportRepository(),
            new FakeBookFileNameTemplateProvider("{{name}} 作者：{{author}}"),
            new BookFileNameMetadataParser());

        var analysis = await service.AnalyzeAsync(new BookImportRequest("demo.txt", null), progress: null, CancellationToken.None);

        Assert.Equal(BookImportAnalysisStatus.Failed, analysis.Status);
        Assert.Equal(BookImportFailureReason.DuplicateBook, analysis.FailureReason);
        Assert.Equal("book-42", analysis.ExistingBookId);
    }

    [Fact]
    public async Task CommitAsync_throws_when_analysis_is_not_ready()
    {
        var service = new BookImportService(
            new FakeTextFileAnalyzer(new TextFileAnalysis("utf-8", "preview", "第一章 开始\n正文")),
            new FakeTextNormalizer("第一章 开始\n正文"),
            new FakeContentHasher("hash"),
            new FakeDuplicateDetector(null),
            new FakeChapterRuleRepository([]),
            new FakeChapterSplitter([]),
            new FakeBookFileStore(),
            new FakeBookImportRepository(),
            new FakeBookFileNameTemplateProvider("{{name}} 作者：{{author}}"),
            new BookFileNameMetadataParser());

        var analysis = new BookImportAnalysis(
            BookImportAnalysisStatus.Failed,
            "demo.txt",
            "demo.txt",
            "demo",
            "utf-8",
            "preview",
            "正文",
            "hash",
            [],
            BookImportFailureReason.NoValidChapters,
            null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CommitAsync(analysis, progress: null, CancellationToken.None));
    }

    [Fact]
    public async Task AnalyzeAsync_passes_manual_encoding_override_to_text_analyzer()
    {
        var analyzer = new FakeTextFileAnalyzer(new TextFileAnalysis("utf-16le", "preview", "第一章 开始\n正文"));
        var service = new BookImportService(
            analyzer,
            new FakeTextNormalizer("第一章 开始\n正文"),
            new FakeContentHasher("hash"),
            new FakeDuplicateDetector(null),
            new FakeChapterRuleRepository([]),
            new FakeChapterSplitter([new BookImportChapter(0, 0, "全文", 0, 2)]),
            new FakeBookFileStore(),
            new FakeBookImportRepository(),
            new FakeBookFileNameTemplateProvider("{{name}} 作者：{{author}}"),
            new BookFileNameMetadataParser());

        var analysis = await service.AnalyzeAsync(new BookImportRequest("demo.txt", "utf-16le"), progress: null, CancellationToken.None);

        Assert.Equal("utf-16le", analyzer.LastRequest?.EncodingOverride);
        Assert.Equal(BookImportAnalysisStatus.ReadyToCommit, analysis.Status);
    }

    [Fact]
    public async Task AnalyzeAsync_uses_template_metadata_when_match_succeeds()
    {
        var service = new BookImportService(
            new FakeTextFileAnalyzer(new TextFileAnalysis("utf-8", "preview", "第一章 开始\n正文")),
            new FakeTextNormalizer("第一章 开始\n正文"),
            new FakeContentHasher("hash"),
            new FakeDuplicateDetector(null),
            new FakeChapterRuleRepository([]),
            new FakeChapterSplitter([new BookImportChapter(0, 0, "全文", 0, 2)]),
            new FakeBookFileStore(),
            new FakeBookImportRepository(),
            new FakeBookFileNameTemplateProvider("{{name}} 作者：{{author}}"),
            new BookFileNameMetadataParser());

        var analysis = await service.AnalyzeAsync(
            new BookImportRequest("信息全知者 作者：魔性沧月.txt", null),
            progress: null,
            CancellationToken.None);

        Assert.Equal("信息全知者", analysis.SuggestedTitle);
        Assert.Equal("魔性沧月", analysis.SuggestedAuthor);
        Assert.True(analysis.IsFileNameTemplateMatched);
    }

    [Fact]
    public async Task AnalyzeAsync_falls_back_to_file_name_when_template_does_not_match()
    {
        var service = new BookImportService(
            new FakeTextFileAnalyzer(new TextFileAnalysis("utf-8", "preview", "第一章 开始\n正文")),
            new FakeTextNormalizer("第一章 开始\n正文"),
            new FakeContentHasher("hash"),
            new FakeDuplicateDetector(null),
            new FakeChapterRuleRepository([]),
            new FakeChapterSplitter([new BookImportChapter(0, 0, "全文", 0, 2)]),
            new FakeBookFileStore(),
            new FakeBookImportRepository(),
            new FakeBookFileNameTemplateProvider("{{name}} 作者：{{author}}"),
            new BookFileNameMetadataParser());

        var analysis = await service.AnalyzeAsync(
            new BookImportRequest("信息全知者-魔性沧月.txt", null),
            progress: null,
            CancellationToken.None);

        Assert.Equal("信息全知者-魔性沧月", analysis.SuggestedTitle);
        Assert.Null(analysis.SuggestedAuthor);
        Assert.False(analysis.IsFileNameTemplateMatched);
    }

    [Fact]
    public async Task CommitAsync_sets_last_import_time_and_preserves_sort_order()
    {
        var repository = new CapturingBookImportRepository();
        var fileStore = new FakeBookFileStore();
        var service = new BookImportService(
            new FakeTextFileAnalyzer(new TextFileAnalysis("utf-8", "preview", "第一章 开始\n正文")),
            new FakeTextNormalizer("第一章 开始\n正文"),
            new FakeContentHasher("hash"),
            new FakeDuplicateDetector(null),
            new FakeChapterRuleRepository([]),
            new FakeChapterSplitter([]),
            fileStore,
            repository,
            new FakeBookFileNameTemplateProvider("{{name}} 作者：{{author}}"),
            new BookFileNameMetadataParser());

        var analysis = new BookImportAnalysis(
            BookImportAnalysisStatus.ReadyToCommit,
            "demo.txt",
            "demo.txt",
            "demo",
            "utf-8",
            "preview",
            "第一章 开始\n正文",
            "hash",
            [new BookImportChapter(0, 42, "第一章 开始", 6, 2)],
            null,
            null,
            "魔性沧月",
            true);

        await service.CommitAsync(analysis, progress: null, CancellationToken.None);

        Assert.NotNull(repository.SavedBook);
        Assert.NotNull(repository.SavedChapters);
        Assert.Equal("第一章 开始\n正文", fileStore.LastNormalizedText);
        Assert.Equal(repository.SavedBook!.ImportedAt, repository.SavedBook.LastImportedAt);
        Assert.Null(repository.SavedBook.LastPlayedAt);
        Assert.Equal("魔性沧月", repository.SavedBook.Author);
        Assert.Single(repository.SavedChapters!);
        Assert.Equal(42, repository.SavedChapters[0].SortOrder);
        Assert.Equal(6, repository.SavedChapters[0].StartOffset);
        Assert.Equal(2, repository.SavedChapters[0].Length);
    }

    private sealed class FakeTextFileAnalyzer : ITextFileAnalyzer
    {
        private readonly TextFileAnalysis _result;
        public BookImportRequest? LastRequest { get; private set; }

        public FakeTextFileAnalyzer(TextFileAnalysis result)
        {
            _result = result;
        }

        public Task<TextFileAnalysis> AnalyzeAsync(
            BookImportRequest request,
            IProgress<BookImportProgress>? progress,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(_result);
        }
    }

    private sealed class FakeTextNormalizer : ITextNormalizer
    {
        private readonly string _normalizedText;

        public FakeTextNormalizer(string normalizedText)
        {
            _normalizedText = normalizedText;
        }

        public string Normalize(string rawText) => _normalizedText;
    }

    private sealed class FakeContentHasher : IContentHasher
    {
        private readonly string _hash;

        public FakeContentHasher(string hash)
        {
            _hash = hash;
        }

        public Task<string> ComputeFileHashAsync(
            string filePath,
            IProgress<BookImportProgress>? progress,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_hash);
        }
    }

    private sealed class FakeDuplicateDetector : IBookDuplicateDetector
    {
        private readonly string? _existingId;

        public FakeDuplicateDetector(string? existingId)
        {
            _existingId = existingId;
        }

        public Task<string?> FindExistingBookIdAsync(string sourceHash, CancellationToken cancellationToken)
        {
            return Task.FromResult(_existingId);
        }
    }

    private sealed class FakeChapterRuleRepository : IChapterRuleRepository
    {
        private readonly IReadOnlyList<ChapterRule> _rules;

        public FakeChapterRuleRepository(IReadOnlyList<ChapterRule> rules)
        {
            _rules = rules;
        }

        public Task<IReadOnlyList<ChapterRule>> GetAllAsync(CancellationToken cancellationToken) => Task.FromResult(_rules);
        public Task<IReadOnlyList<ChapterRule>> GetEnabledAsync(CancellationToken cancellationToken) => Task.FromResult(_rules);
        public Task SaveAsync(ChapterRule rule, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DeleteAsync(string ruleId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task MoveAsync(string ruleId, int newSortOrder, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<int> ImportDefaultsAsync(CancellationToken cancellationToken) => Task.FromResult(0);
    }

    private sealed class FakeChapterSplitter : IChapterSplitter
    {
        private readonly IReadOnlyList<BookImportChapter> _chapters;

        public FakeChapterSplitter(IReadOnlyList<BookImportChapter> chapters)
        {
            _chapters = chapters;
        }

        public IReadOnlyList<BookImportChapter> Split(string normalizedText, IReadOnlyList<ChapterRule> rules) => _chapters;
    }

    private sealed class FakeBookFileStore : IBookFileStore
    {
        public string? LastNormalizedText { get; private set; }

        public Task<BookFileCopyHandle> StageNormalizedTextAsync(
            string normalizedText,
            string bookId,
            IProgress<BookImportProgress>? progress,
            CancellationToken cancellationToken)
        {
            LastNormalizedText = normalizedText;
            return Task.FromResult(new BookFileCopyHandle($"Books/{bookId}/content.txt", $"Books/{bookId}/content.txt.tmp"));
        }

        public Task FinalizeAsync(BookFileCopyHandle copyHandle, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task CleanupAsync(BookFileCopyHandle copyHandle) => Task.CompletedTask;
    }

    private sealed class FakeBookImportRepository : IBookImportRepository
    {
        public Task SaveAsync(Book book, IReadOnlyList<Chapter> chapters, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeBookFileNameTemplateProvider : IBookFileNameTemplateProvider
    {
        private readonly string _template;

        public FakeBookFileNameTemplateProvider(string template)
        {
            _template = template;
        }

        public Task<string> GetCurrentTemplateAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(_template);
        }
    }

    private sealed class CapturingBookImportRepository : IBookImportRepository
    {
        public Book? SavedBook { get; private set; }
        public IReadOnlyList<Chapter>? SavedChapters { get; private set; }

        public Task SaveAsync(Book book, IReadOnlyList<Chapter> chapters, CancellationToken cancellationToken)
        {
            SavedBook = book;
            SavedChapters = chapters;
            return Task.CompletedTask;
        }
    }
}
