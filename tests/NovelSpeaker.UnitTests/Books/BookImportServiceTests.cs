using NovelSpeaker.Application.Books;
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
        var splitter = new FakeChapterSplitter([new BookImportChapter(0, "第一章 开始", "正文", 6, 2)]);
        var service = new BookImportService(analyzer, normalizer, hasher, duplicates, rules, splitter, new FakeBookFileStore(), new FakeBookImportRepository());

        var analysis = await service.AnalyzeAsync("demo.txt", null, CancellationToken.None);

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
            new FakeBookImportRepository());

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

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CommitAsync(analysis, CancellationToken.None));
    }

    private sealed class FakeTextFileAnalyzer : ITextFileAnalyzer
    {
        private readonly TextFileAnalysis _result;

        public FakeTextFileAnalyzer(TextFileAnalysis result)
        {
            _result = result;
        }

        public Task<TextFileAnalysis> AnalyzeAsync(string filePath, string? encodingName, CancellationToken cancellationToken)
        {
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

        public Task<string> ComputeFileHashAsync(string filePath, CancellationToken cancellationToken)
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
        public Task<BookFileCopyHandle> PrepareCopyAsync(string sourceFilePath, string bookId, CancellationToken cancellationToken)
        {
            return Task.FromResult(new BookFileCopyHandle($"Books/{bookId}/original.txt", $"Books/{bookId}/original.txt.tmp"));
        }

        public Task FinalizeAsync(BookFileCopyHandle copyHandle, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task CleanupAsync(BookFileCopyHandle copyHandle) => Task.CompletedTask;
    }

    private sealed class FakeBookImportRepository : IBookImportRepository
    {
        public Task SaveAsync(Book book, IReadOnlyList<Chapter> chapters, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
