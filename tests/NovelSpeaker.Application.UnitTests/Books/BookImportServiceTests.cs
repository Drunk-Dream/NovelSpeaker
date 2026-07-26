using System.Text;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Books.Import;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Domain.Books;
using NovelSpeaker.TestKit.Common;
using Xunit;

namespace NovelSpeaker.Application.UnitTests.Books;

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

        var result = await service.ImportAsync(new DirectBookImportRequest("demo.txt", null, "demo.txt"), progress: null, CancellationToken.None);

        Assert.Equal(DirectBookImportStatus.Failed, result.Status);
        Assert.Equal(BookImportFailureReason.DuplicateBook, result.FailureReason);
    }

    [Fact]
    public async Task ImportAsync_returns_encoding_selection_when_analysis_is_low_confidence()
    {
        var service = CreateService(
            analyzer: new FakeTextFileAnalyzer(new TextFileAnalysis(
                "demo.txt",
                "demo",
                "gb18030",
                "preview",
                "第一章 开始\n正文",
                TextEncodingDetectionMode.Gb18030Fallback,
                true,
                LowConfidenceReason.FallbackEncoding)),
            splitter: new FakeChapterSplitter([new BookImportChapter(0, 0, "第一章 开始", 6, 2)]));

        var result = await service.ImportAsync(new DirectBookImportRequest("demo.txt", null, "demo.txt"), progress: null, CancellationToken.None);

        Assert.Equal(DirectBookImportStatus.RequiresEncodingSelection, result.Status);
        Assert.NotNull(result.EncodingSelectionPrompt);
        Assert.Equal("gb18030", result.EncodingSelectionPrompt!.DefaultEncoding);
    }

    [Fact]
    public async Task ImportAsync_returns_encoding_selection_with_source_name_when_detection_fails()
    {
        var service = CreateService(analyzer: new DecoderFailingTextFileAnalyzer());

        var result = await service.ImportAsync(
            new DirectBookImportRequest("external.txt", null, "external.txt"),
            progress: null,
            CancellationToken.None);

        Assert.Equal(DirectBookImportStatus.RequiresEncodingSelection, result.Status);
        Assert.Equal("external.txt", result.EncodingSelectionPrompt?.FileName);
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

        var result = await service.ImportAsync(new DirectBookImportRequest("demo.txt", "utf-16le", "demo.txt"), progress: null, CancellationToken.None);

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
            analyzer: new FakeTextFileAnalyzer(CreateAnalysis(
                "utf-8",
                sourceFileName: "信息全知者 作者：魔性沧月.txt")),
            repository: repository,
            splitter: new FakeChapterSplitter([new BookImportChapter(0, 0, "全文", 0, 2)]));

        var result = await service.ImportAsync(
            new DirectBookImportRequest("信息全知者 作者：魔性沧月.txt", null, "信息全知者 作者：魔性沧月.txt"),
            progress: null,
            CancellationToken.None);

        Assert.Equal(DirectBookImportStatus.Imported, result.Status);
        Assert.NotNull(repository.SavedBook);
        Assert.Equal("信息全知者", repository.SavedBook!.Title);
        Assert.Equal("魔性沧月", repository.SavedBook.Author);
    }

    [Fact]
    public async Task ImportAsync_cleans_up_staged_file_without_finalizing_when_repository_save_fails()
    {
        var fileStore = new FakeBookFileStore();
        var service = CreateService(
            fileStore: fileStore,
            repository: new ThrowingBookImportRepository(),
            splitter: new FakeChapterSplitter([new BookImportChapter(0, 0, "全文", 0, 2)]));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ImportAsync(new DirectBookImportRequest("demo.txt", null, "demo.txt"), progress: null, CancellationToken.None));

        Assert.False(fileStore.FinalizeCalled);
        Assert.True(fileStore.CleanupCalled);
        Assert.True(fileStore.CleanupIncludedFinalFile);
        Assert.False(fileStore.CleanupCancellationToken.CanBeCanceled);
    }

    [Fact]
    public async Task ImportAsync_leaves_database_committed_import_for_recovery_when_finalize_fails()
    {
        var fileStore = new FakeBookFileStore
        {
            FinalizeException = new IOException("finalize failed")
        };
        var repository = new CapturingBookImportRepository();
        var service = CreateService(
            fileStore: fileStore,
            repository: repository,
            splitter: new FakeChapterSplitter([new BookImportChapter(0, 0, "全文", 0, 2)]));

        var result = await service.ImportAsync(
            new DirectBookImportRequest("external-source.txt", null, "external-source.txt"),
            progress: null,
            CancellationToken.None);

        Assert.Equal(DirectBookImportStatus.Failed, result.Status);
        Assert.Equal(BookImportFailureReason.FileReadFailed, result.FailureReason);
        Assert.True(fileStore.FinalizeCalled);
        Assert.False(fileStore.CleanupCalled);
        Assert.NotNull(repository.SavedBook);
    }

    [Fact]
    public async Task ImportAsync_uses_injected_time_and_identifier_sequence()
    {
        var repository = new CapturingBookImportRepository();
        var now = new DateTimeOffset(2026, 7, 17, 10, 30, 0, TimeSpan.Zero);
        var service = CreateService(
            repository: repository,
            splitter: new FakeChapterSplitter(
            [
                new BookImportChapter(0, 0, "第一章", 0, 2),
                new BookImportChapter(1, 1, "第二章", 2, 2)
            ]),
            timeProvider: new FixedTimeProvider(now),
            idGenerator: new SequenceBookImportIdGenerator("book-fixed", "chapter-1", "chapter-2"));

        var result = await service.ImportAsync(
            new DirectBookImportRequest("demo.txt", null, "demo.txt"),
            progress: null,
            CancellationToken.None);

        Assert.Equal("book-fixed", result.ImportedBook?.BookId);
        Assert.Equal(now, repository.SavedBook?.ImportedAt);
        Assert.Equal(now, repository.SavedBook?.UpdatedAt);
        Assert.Equal(now, repository.SavedBook?.LastImportedAt);
        Assert.Equal(["chapter-1", "chapter-2"], repository.SavedChapters?.Select(chapter => chapter.Id));
    }

    [Fact]
    public async Task ImportAsync_persists_staged_database_committed_and_completed_phases_in_order()
    {
        var journal = new FakeBookOperationJournal();
        var service = CreateService(journal: journal);

        var result = await service.ImportAsync(
            new DirectBookImportRequest("demo.txt", null, "demo.txt"),
            progress: null,
            CancellationToken.None);

        Assert.Equal(DirectBookImportStatus.Imported, result.Status);
        Assert.NotNull(journal.CreatedOperation);
        Assert.Equal(BookOperationPhase.Staged, journal.CreatedOperation!.Phase);
        Assert.Equal(
            [BookOperationPhase.DatabaseCommitted, BookOperationPhase.Completed],
            journal.Phases);
    }

    [Fact]
    public async Task ImportAsync_does_not_remove_files_after_database_commit_when_phase_update_fails()
    {
        var fileStore = new FakeBookFileStore();
        var journal = new FakeBookOperationJournal { FailOnPhase = BookOperationPhase.DatabaseCommitted };
        var repository = new CapturingBookImportRepository();
        var service = CreateService(fileStore: fileStore, repository: repository, journal: journal);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ImportAsync(
            new DirectBookImportRequest("demo.txt", null, "demo.txt"),
            progress: null,
            CancellationToken.None));

        Assert.NotNull(repository.SavedBook);
        Assert.False(fileStore.FinalizeCalled);
        Assert.False(fileStore.CleanupCalled);
    }

    [Fact]
    public async Task ImportAsync_propagates_cancellation_from_semantic_port()
    {
        var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var service = CreateService(analyzer: new CancelingTextFileAnalyzer());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.ImportAsync(new DirectBookImportRequest("demo.txt", null, "demo.txt"), progress: null, cancellation.Token));
    }

    [Fact]
    public async Task ImportAsync_cleans_up_final_file_and_propagates_cancellation_during_commit()
    {
        var cancellation = new CancellationTokenSource();
        var fileStore = new FakeBookFileStore();
        var service = CreateService(
            fileStore: fileStore,
            repository: new CancelingBookImportRepository(cancellation),
            splitter: new FakeChapterSplitter([new BookImportChapter(0, 0, "全文", 0, 2)]));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.ImportAsync(new DirectBookImportRequest("demo.txt", null, "demo.txt"), progress: null, cancellation.Token));

        Assert.False(fileStore.FinalizeCalled);
        Assert.True(fileStore.CleanupCalled);
        Assert.True(fileStore.CleanupIncludedFinalFile);
        Assert.False(fileStore.CleanupCancellationToken.CanBeCanceled);
    }

    private static DirectBookImportService CreateService(
        ITextFileAnalyzer? analyzer = null,
        FakeTextNormalizer? normalizer = null,
        FakeContentHasher? hasher = null,
        FakeDuplicateDetector? duplicates = null,
        FakeChapterRuleRepository? rules = null,
        FakeChapterSplitter? splitter = null,
        FakeBookFileStore? fileStore = null,
        IBookImportRepository? repository = null,
        IBookOperationJournal? journal = null,
        TimeProvider? timeProvider = null,
        IBookImportIdGenerator? idGenerator = null)
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
            journal ?? new FakeBookOperationJournal(),
            new FakeBookFileNameTemplateProvider("{{name}} 作者：{{author}}"),
            new BookFileNameMetadataParser(),
            timeProvider ?? TimeProvider.System,
            idGenerator ?? new SequenceBookImportIdGenerator("book-id", "chapter-id"));
    }

    private static TextFileAnalysis CreateAnalysis(
        string encoding,
        TextEncodingDetectionMode detectionMode = TextEncodingDetectionMode.StrictUtf8,
        string sourceFileName = "demo.txt")
    {
        return new TextFileAnalysis(
            sourceFileName,
            sourceFileName[..^4],
            encoding,
            "preview",
            "第一章 开始\n正文",
            detectionMode,
            false,
            null);
    }

    private sealed class CancelingTextFileAnalyzer : ITextFileAnalyzer
    {
        public Task<TextFileAnalysis> AnalyzeAsync(
            BookImportRequest request,
            IProgress<BookImportProgress>? progress,
            CancellationToken cancellationToken) => Task.FromCanceled<TextFileAnalysis>(cancellationToken);
    }

    private sealed class DecoderFailingTextFileAnalyzer : ITextFileAnalyzer
    {
        public Task<TextFileAnalysis> AnalyzeAsync(
            BookImportRequest request,
            IProgress<BookImportProgress>? progress,
            CancellationToken cancellationToken) => throw new DecoderFallbackException();
    }

    private sealed class SequenceBookImportIdGenerator(params string[] ids) : IBookImportIdGenerator
    {
        private readonly Queue<string> _ids = new(ids);

        public string CreateBookId() => _ids.Dequeue();

        public string CreateChapterId() => _ids.Dequeue();

        public string CreateOperationId() => "operation-id";
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
        public CancellationToken CleanupCancellationToken { get; private set; }
        public Exception? FinalizeException { get; init; }

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
            if (FinalizeException is not null)
            {
                throw FinalizeException;
            }

            return Task.CompletedTask;
        }

        public Task CleanupAsync(
            BookFileCopyHandle copyHandle,
            bool includeFinalFile,
            CancellationToken cancellationToken)
        {
            CleanupCalled = true;
            CleanupIncludedFinalFile = includeFinalFile;
            CleanupCancellationToken = cancellationToken;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeBookImportRepository : IBookImportRepository
    {
        public Task SaveAsync(Book book, IReadOnlyList<Chapter> chapters, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeBookOperationJournal : IBookOperationJournal
    {
        public BookOperationRecord? CreatedOperation { get; private set; }

        public List<BookOperationPhase> Phases { get; } = [];

        public BookOperationPhase? FailOnPhase { get; init; }

        public Task CreateAsync(BookOperationRecord operation, CancellationToken cancellationToken)
        {
            CreatedOperation = operation;
            return Task.CompletedTask;
        }

        public Task SetPhaseAsync(string operationId, BookOperationPhase phase, CancellationToken cancellationToken)
        {
            Phases.Add(phase);
            if (phase == FailOnPhase)
            {
                throw new InvalidOperationException("phase failed");
            }

            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<BookOperationRecord>> GetIncompleteAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<BookOperationRecord>>([]);
    }

    private sealed class ThrowingBookImportRepository : IBookImportRepository
    {
        public Task SaveAsync(Book book, IReadOnlyList<Chapter> chapters, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("save failed");
        }
    }

    private sealed class CancelingBookImportRepository(CancellationTokenSource cancellation) : IBookImportRepository
    {
        public Task SaveAsync(Book book, IReadOnlyList<Chapter> chapters, CancellationToken cancellationToken)
        {
            cancellation.Cancel();
            return Task.FromCanceled(cancellationToken);
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
