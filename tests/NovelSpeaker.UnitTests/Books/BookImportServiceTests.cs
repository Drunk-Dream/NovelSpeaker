using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Domain.Books;
using NovelSpeaker.Infrastructure.Books;
using Xunit;

namespace NovelSpeaker.UnitTests.Books;

public sealed class BookImportServiceTests
{
    [Fact]
    public async Task ImportAsync_returns_duplicate_failure_when_hash_already_exists()
    {
        var service = CreateService(
            analyzer: new FakeTextFileAnalyzer(CreateAnalysis("utf-8")),
            hasher: new FakeContentHasher("same-hash"),
            duplicates: new FakeDuplicateDetector("book-42"),
            splitter: new FakeChapterSplitter([new BookImportChapter(0, 0, "第一章 开始", 6, 2)]));

        var result = await service.ImportAsync(new DirectBookImportRequest("demo.txt", null), progress: null, CancellationToken.None);

        Assert.Equal(DirectBookImportStatus.Failed, result.Status);
        Assert.Equal(BookImportFailureReason.DuplicateBook, result.FailureReason);
    }

    [Fact]
    public async Task ImportAsync_returns_encoding_selection_when_analysis_is_low_confidence()
    {
        var service = CreateService(
            analyzer: new FakeTextFileAnalyzer(new TextFileAnalysis(
                "gb18030",
                "preview",
                "第一章 开始\n正文",
                TextEncodingDetectionMode.Gb18030Fallback,
                true,
                LowConfidenceReason.FallbackEncoding)),
            splitter: new FakeChapterSplitter([new BookImportChapter(0, 0, "第一章 开始", 6, 2)]));

        var result = await service.ImportAsync(new DirectBookImportRequest("demo.txt", null), progress: null, CancellationToken.None);

        Assert.Equal(DirectBookImportStatus.RequiresEncodingSelection, result.Status);
        Assert.NotNull(result.EncodingSelectionPrompt);
        Assert.Equal("gb18030", result.EncodingSelectionPrompt!.DefaultEncoding);
    }

    [Fact]
    public async Task ImportAsync_passes_manual_encoding_override_to_text_analyzer_and_imports()
    {
        var analyzer = new FakeTextFileAnalyzer(CreateAnalysis("utf-16le", TextEncodingDetectionMode.ManualOverride));
        var repository = new CapturingBookImportRepository();
        var service = CreateService(
            analyzer: analyzer,
            repository: repository,
            splitter: new FakeChapterSplitter([new BookImportChapter(0, 42, "全文", 0, 2)]));

        var result = await service.ImportAsync(new DirectBookImportRequest("demo.txt", "utf-16le"), progress: null, CancellationToken.None);

        Assert.Equal("utf-16le", analyzer.LastRequest?.EncodingOverride);
        Assert.Equal(DirectBookImportStatus.Imported, result.Status);
        Assert.NotNull(repository.SavedBook);
        Assert.Equal("utf-16le", repository.SavedBook!.Encoding);
    }

    [Fact]
    public async Task ImportAsync_uses_template_metadata_when_match_succeeds()
    {
        var repository = new CapturingBookImportRepository();
        var service = CreateService(
            repository: repository,
            splitter: new FakeChapterSplitter([new BookImportChapter(0, 0, "全文", 0, 2)]));

        var result = await service.ImportAsync(
            new DirectBookImportRequest("信息全知者 作者：魔性沧月.txt", null),
            progress: null,
            CancellationToken.None);

        Assert.Equal(DirectBookImportStatus.Imported, result.Status);
        Assert.NotNull(repository.SavedBook);
        Assert.Equal("信息全知者", repository.SavedBook!.Title);
        Assert.Equal("魔性沧月", repository.SavedBook.Author);
    }

    [Fact]
    public async Task ImportAsync_cleans_up_final_file_when_repository_save_fails()
    {
        var fileStore = new FakeBookFileStore();
        var service = CreateService(
            fileStore: fileStore,
            repository: new ThrowingBookImportRepository(),
            splitter: new FakeChapterSplitter([new BookImportChapter(0, 0, "全文", 0, 2)]));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ImportAsync(new DirectBookImportRequest("demo.txt", null), progress: null, CancellationToken.None));

        Assert.True(fileStore.FinalizeCalled);
        Assert.True(fileStore.CleanupCalled);
        Assert.True(fileStore.CleanupIncludedFinalFile);
    }

    private static DirectBookImportService CreateService(
        FakeTextFileAnalyzer? analyzer = null,
        FakeTextNormalizer? normalizer = null,
        FakeContentHasher? hasher = null,
        FakeDuplicateDetector? duplicates = null,
        FakeChapterRuleRepository? rules = null,
        FakeChapterSplitter? splitter = null,
        FakeBookFileStore? fileStore = null,
        IBookImportRepository? repository = null)
    {
        return new DirectBookImportService(
            analyzer ?? new FakeTextFileAnalyzer(CreateAnalysis("utf-8")),
            normalizer ?? new FakeTextNormalizer("第一章 开始\n正文"),
            hasher ?? new FakeContentHasher("hash"),
            duplicates ?? new FakeDuplicateDetector(null),
            rules ?? new FakeChapterRuleRepository([]),
            splitter ?? new FakeChapterSplitter([new BookImportChapter(0, 0, "全文", 0, 2)]),
            fileStore ?? new FakeBookFileStore(),
            repository ?? new FakeBookImportRepository(),
            new FakeBookFileNameTemplateProvider("{{name}} 作者：{{author}}"),
            new BookFileNameMetadataParser());
    }

    private static TextFileAnalysis CreateAnalysis(
        string encoding,
        TextEncodingDetectionMode detectionMode = TextEncodingDetectionMode.StrictUtf8)
    {
        return new TextFileAnalysis(
            encoding,
            "preview",
            "第一章 开始\n正文",
            detectionMode,
            false,
            null);
    }

    private sealed class FakeTextFileAnalyzer : ITextFileAnalyzer
    {
        private readonly TextFileAnalysis _result;

        public FakeTextFileAnalyzer(TextFileAnalysis result)
        {
            _result = result;
        }

        public BookImportRequest? LastRequest { get; private set; }

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
        public Task SaveOrderAsync(IReadOnlyList<(string RuleId, int SortOrder)> order, CancellationToken cancellationToken) => Task.CompletedTask;
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
        public bool FinalizeCalled { get; private set; }
        public bool CleanupCalled { get; private set; }
        public bool CleanupIncludedFinalFile { get; private set; }

        public Task<BookFileCopyHandle> StageNormalizedTextAsync(
            string normalizedText,
            string bookId,
            IProgress<BookImportProgress>? progress,
            CancellationToken cancellationToken)
        {
            LastNormalizedText = normalizedText;
            return Task.FromResult(new BookFileCopyHandle($"Books/{bookId}/content.txt", $"Books/{bookId}/content.txt.tmp"));
        }

        public Task FinalizeAsync(BookFileCopyHandle copyHandle, CancellationToken cancellationToken)
        {
            FinalizeCalled = true;
            return Task.CompletedTask;
        }

        public Task CleanupAsync(BookFileCopyHandle copyHandle, bool includeFinalFile)
        {
            CleanupCalled = true;
            CleanupIncludedFinalFile = includeFinalFile;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeBookImportRepository : IBookImportRepository
    {
        public Task SaveAsync(Book book, IReadOnlyList<Chapter> chapters, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class ThrowingBookImportRepository : IBookImportRepository
    {
        public Task SaveAsync(Book book, IReadOnlyList<Chapter> chapters, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("save failed");
        }
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
