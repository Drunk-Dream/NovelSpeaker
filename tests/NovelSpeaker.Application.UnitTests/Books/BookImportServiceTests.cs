using System.Text;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Books.Import;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Domain.Books;
using NovelSpeaker.Domain.Settings;
using NovelSpeaker.TestKit.Common;
using Xunit;

namespace NovelSpeaker.Application.UnitTests.Books;

public sealed class BookImportServiceTests
{
    [Fact]
    public async Task ImportAsync_returns_stable_failure_when_chapter_rule_times_out()
    {
        var rule = new ChapterRule("rule", "复杂规则", @"^(a+)+$", 10, true,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        var service = CreateService(
            normalizer: new FakeTextNormalizer(new string('a', 100_000) + "!"),
            splitter: new ChapterSplitter(),
            rules: new FakeChapterRuleRepository([rule]));

        var result = await service.ImportAsync(
            new DirectBookImportRequest("demo.txt", null, "demo.txt"), null, CancellationToken.None);

        Assert.Equal(DirectBookImportStatus.Failed, result.Status);
        Assert.Equal(BookImportFailureReason.ChapterRuleTimedOut, result.FailureReason);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ImportAsync_notifies_committed_active_catalog_changes_and_isolates_observer_failures(bool active)
    {
        var target = ExistingTarget("existing");
        if (!active) target = target with { Book = target.Book with { ActiveSourceId = null } };
        var repository = new CapturingBookImportRepository { Target = target };
        var changes = new BookSourceChanges();
        var service = CreateService(repository: repository, sourceChanges: changes);
        var committed = new List<BookCommittedChange>();
        changes.Changed += (_, _) => throw new InvalidOperationException("observer failure");
        changes.Changed += (_, value) =>
        {
            Assert.NotNull(repository.SavedSnapshot);
            committed.Add(value);
        };
        var result = await service.ImportAsync(new DirectBookImportRequest("demo.txt", null, "demo.txt", TargetBookId: "existing"),
            null, CancellationToken.None);
        Assert.Equal(DirectBookImportStatus.Imported, result.Status);
        if (active)
        {
            Assert.Equal(2, committed.Count);
            Assert.Equal(new BookCommittedChange.MetadataCommitted("existing"), committed[0]);
            var change = Assert.IsType<BookCommittedChange.ActiveCatalogCommitted>(committed[1]);
            Assert.Equal("existing", change.BookId);
            Assert.Equal("existing-source", change.SourceId);
            Assert.Equal(repository.SavedSnapshot!.Catalog[0].Id, change.CatalogVersion);
        }
        else Assert.Empty(committed);
    }

    [Fact]
    public async Task ImportAsync_publishes_initial_metadata_activation_and_catalog_after_commit()
    {
        var repository = new CapturingBookImportRepository();
        var changes = new BookSourceChanges();
        var committed = new List<BookCommittedChange>();
        var observedAfterCommit = true;
        changes.Changed += (_, _) => throw new InvalidOperationException("observer failure");
        changes.Changed += (_, value) =>
        {
            observedAfterCommit &= repository.SavedSnapshot is not null;
            committed.Add(value);
        };
        var service = CreateService(repository: repository, sourceChanges: changes,
            journal: new FakeBookOperationJournal { FailOnPhase = BookOperationPhase.Completed });

        var result = await service.ImportAsync(new DirectBookImportRequest("demo.txt", null, "demo.txt"), null, CancellationToken.None);

        Assert.Equal(DirectBookImportStatus.Imported, result.Status);
        Assert.True(observedAfterCommit);
        Assert.Equal<BookCommittedChange>([
            new BookCommittedChange.MetadataCommitted("book-id"),
            new BookCommittedChange.ActiveSourceChanged("book-id", null, "local-source-id"),
            new BookCommittedChange.ActiveCatalogCommitted("book-id", "local-source-id", "chapter-id")], committed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ImportAsync_does_not_publish_when_file_or_database_commit_fails(bool fileFailure)
    {
        var changes = new BookSourceChanges();
        var committed = new List<BookCommittedChange>();
        changes.Changed += (_, value) => committed.Add(value);
        var service = CreateService(sourceChanges: changes,
            fileStore: new FakeBookFileStore { FinalizeException = fileFailure ? new InvalidOperationException("file failed") : null },
            repository: new ThrowingBookImportRepository());

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ImportAsync(
            new DirectBookImportRequest("demo.txt", null, "demo.txt"), null, CancellationToken.None));

        Assert.Empty(committed);
    }

    [Fact]
    public async Task ImportAsync_waits_for_repository_commit_before_publishing()
    {
        var repository = new DeferredBookImportRepository();
        var changes = new BookSourceChanges();
        var committed = new List<BookCommittedChange>();
        changes.Changed += (_, change) => committed.Add(change);
        var service = CreateService(repository: repository, sourceChanges: changes);

        var import = service.ImportAsync(new DirectBookImportRequest("demo.txt", null, "demo.txt"), null, CancellationToken.None);
        await repository.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Empty(committed);
        repository.AllowCommit.SetResult();
        Assert.Equal(DirectBookImportStatus.Imported, (await import).Status);
        Assert.Equal(3, committed.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ImportAsync_requires_explicit_choice_for_multiple_candidates(bool createNew)
    {
        var candidates = new[]
        {
            new BookImportCandidate("a", "demo", null, DateTimeOffset.UnixEpoch, null),
            new BookImportCandidate("b", "demo", null, DateTimeOffset.UnixEpoch, null)
        };
        var repository = new CapturingBookImportRepository { Candidates = candidates, Target = ExistingTarget("b") };
        var fileStore = new FakeBookFileStore();
        var service = CreateService(repository: repository, fileStore: fileStore);
        var unresolved = await service.ImportAsync(new DirectBookImportRequest("demo.txt", null, "demo.txt"), null, CancellationToken.None);
        Assert.Equal(DirectBookImportStatus.RequiresBookSelection, unresolved.Status);
        Assert.Equal(candidates, unresolved.BookCandidates);
        Assert.Null(repository.SavedSnapshot);
        Assert.Null(fileStore.LastNormalizedText);

        var resolved = await service.ImportAsync(new DirectBookImportRequest("demo.txt", null, "demo.txt",
            TargetBookId: createNew ? null : "b", CreateNewBook: createNew), null, CancellationToken.None);
        Assert.Equal(DirectBookImportStatus.Imported, resolved.Status);
        Assert.Equal(createNew ? "book-id" : "b", resolved.ImportedBook?.BookId);
        Assert.Equal(createNew, repository.SavedSnapshot?.IsNewBook);
    }

    [Fact]
    public async Task ImportAsync_matches_blank_author_and_reuses_the_existing_local_source()
    {
        var repository = new CapturingBookImportRepository
        {
            Candidates = [new BookImportCandidate("existing", "demo", null, DateTimeOffset.UnixEpoch, null)],
            Target = ExistingTarget("existing")
        };
        var service = CreateService(repository: repository);
        var result = await service.ImportAsync(new DirectBookImportRequest("demo.txt", null, "demo.txt"), null, CancellationToken.None);
        Assert.Equal("existing", result.ImportedBook?.BookId);
        Assert.True(string.IsNullOrEmpty(repository.MatchedAuthor));
        Assert.Equal("existing-source", repository.SavedSnapshot?.Source.Id);
        Assert.Equal("Books/existing/content.txt", repository.SavedSnapshot?.ExpectedContentPath);
        Assert.All(repository.SavedSnapshot!.Catalog, chapter => Assert.Equal("existing-source", chapter.SourceId));
    }

    private static LocalSourceImportTarget ExistingTarget(string id)
    {
        var now = DateTimeOffset.UnixEpoch;
        return new LocalSourceImportTarget(new Book(id, "demo", null, "existing-source", now, null, now),
            new BookSource("existing-source", id, SourceType.Local, "demo", null, "old description", now, now),
            new LocalBookSource("existing-source", "demo.txt", $"Books/{id}/content.txt", "old-hash", "utf-8", now, now));
    }

    [Fact]
    public async Task ImportAsync_creates_a_book_without_using_hash_as_identity()
    {
        var service = CreateService(
            analyzer: new FakeTextFileAnalyzer(CreateAnalysis("utf-8")),
            hasher: new FakeContentHasher("same-hash"),
            splitter: new FakeChapterSplitter([new BookImportChapter(0, 0, "第一章 开始", 6, 2)]));

        var result = await service.ImportAsync(new DirectBookImportRequest("demo.txt", null, "demo.txt"), progress: null, CancellationToken.None);

        Assert.Equal(DirectBookImportStatus.Imported, result.Status);
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
        Assert.Equal("utf-16le", repository.SavedSnapshot!.LocalSource.Encoding);
    }

    [Fact]
    public async Task ImportAsync_uses_filename_metadata_rule_when_match_succeeds()
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
    public async Task ImportAsync_recognizes_header_metadata_without_removing_text_and_obeys_blank_switch()
    {
        const string text = "书名：正文书名\n作者：正文作者\n简介：正文简介\n\n第一章 开始\n正文甲\n\n正文乙\n";
        var repository = new CapturingBookImportRepository();
        var fileStore = new FakeBookFileStore();
        var service = CreateService(
            normalizer: new FakeTextNormalizer(text),
            splitter: new ChapterSplitter(),
            fileStore: fileStore,
            repository: repository,
            fileNameRules: [],
            headerRules:
            [
                new TextHeaderMetadataRule("name", "书名", @"^书名：(?<name>.+)$", 10, true, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch),
                new TextHeaderMetadataRule("author", "作者", @"^作者：(?<author>.+)$", 20, true, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch),
                new TextHeaderMetadataRule("description", "简介", @"^简介：(?<description>.+)$", 30, true, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch)
            ],
            rules: new FakeChapterRuleRepository(
            [
                new ChapterRule("chapter", "章节", @"^第一章 .+$", 10, true, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch)
            ]),
            splitOnBlankLines: true,
            idGenerator: new SequenceBookImportIdGenerator("book-id", "chapter-1", "chapter-2"));

        var result = await service.ImportAsync(
            new DirectBookImportRequest("demo.txt", null, "demo.txt"), null, CancellationToken.None);

        Assert.Equal(DirectBookImportStatus.Imported, result.Status);
        Assert.Equal("正文书名", repository.SavedBook?.Title);
        Assert.Equal("正文作者", repository.SavedBook?.Author);
        Assert.Equal("正文简介", repository.SavedBook?.Description);
        Assert.Equal(text, fileStore.LastNormalizedText);
        Assert.Equal(["第一章 开始", "第 2 节"], repository.SavedChapters?.Select(chapter => chapter.Title));
    }

    [Fact]
    public async Task ImportAsync_removes_the_new_snapshot_when_repository_save_fails()
    {
        var fileStore = new FakeBookFileStore();
        var service = CreateService(
            fileStore: fileStore,
            repository: new ThrowingBookImportRepository(),
            splitter: new FakeChapterSplitter([new BookImportChapter(0, 0, "全文", 0, 2)]));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ImportAsync(new DirectBookImportRequest("demo.txt", null, "demo.txt"), progress: null, CancellationToken.None));

        Assert.True(fileStore.FinalizeCalled);
        Assert.True(fileStore.CleanupCalled);
        Assert.True(fileStore.CleanupIncludedFinalFile);
        Assert.False(fileStore.CleanupCancellationToken.CanBeCanceled);
    }

    [Fact]
    public async Task ImportAsync_does_not_commit_when_finalize_fails()
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
        Assert.True(fileStore.CleanupCalled);
        Assert.Null(repository.SavedBook);
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
        Assert.Equal(now, repository.SavedSnapshot?.LocalSource.LastImportedAt);
        Assert.Equal(["chapter-1", "chapter-2"], repository.SavedChapters?.Select(chapter => chapter.Id));
    }

    [Fact]
    public async Task ImportAsync_completes_the_journal_after_snapshot_commit()
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
        Assert.Contains(BookOperationPhase.Completed, journal.Phases);
    }

    [Fact]
    public async Task ImportAsync_reports_success_and_leaves_recovery_intent_when_completion_recording_fails()
    {
        var fileStore = new FakeBookFileStore();
        var journal = new FakeBookOperationJournal { FailOnPhase = BookOperationPhase.Completed };
        var repository = new CapturingBookImportRepository();
        var service = CreateService(fileStore: fileStore, repository: repository, journal: journal);

        var result = await service.ImportAsync(
            new DirectBookImportRequest("demo.txt", null, "demo.txt"),
            progress: null,
            CancellationToken.None);

        Assert.Equal(DirectBookImportStatus.Imported, result.Status);
        Assert.NotNull(repository.SavedBook);
        Assert.True(fileStore.FinalizeCalled);
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
        var changes = new BookSourceChanges();
        var committed = new List<BookCommittedChange>();
        changes.Changed += (_, value) => committed.Add(value);
        var service = CreateService(
            fileStore: fileStore,
            sourceChanges: changes,
            repository: new CancelingBookImportRepository(cancellation),
            splitter: new FakeChapterSplitter([new BookImportChapter(0, 0, "全文", 0, 2)]));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.ImportAsync(new DirectBookImportRequest("demo.txt", null, "demo.txt"), progress: null, cancellation.Token));

        Assert.True(fileStore.FinalizeCalled);
        Assert.True(fileStore.CleanupCalled);
        Assert.True(fileStore.CleanupIncludedFinalFile);
        Assert.False(fileStore.CleanupCancellationToken.CanBeCanceled);
        Assert.Empty(committed);
    }

    private static DirectBookImportService CreateService(
        ITextFileAnalyzer? analyzer = null,
        FakeTextNormalizer? normalizer = null,
        FakeContentHasher? hasher = null,
        FakeChapterRuleRepository? rules = null,
        IChapterSplitter? splitter = null,
        FakeBookFileStore? fileStore = null,
        IBookImportRepository? repository = null,
        IBookOperationJournal? journal = null,
        IReadOnlyList<FileNameMetadataRule>? fileNameRules = null,
        IReadOnlyList<TextHeaderMetadataRule>? headerRules = null,
        bool splitOnBlankLines = false,
        TimeProvider? timeProvider = null,
        IBookImportIdGenerator? idGenerator = null,
        BookSourceChanges? sourceChanges = null)
    {
        return new DirectBookImportService(
            analyzer ?? new FakeTextFileAnalyzer(CreateAnalysis("utf-8")),
            normalizer ?? new FakeTextNormalizer("第一章 开始\n正文"),
            hasher ?? new FakeContentHasher("hash"),
            rules ?? new FakeChapterRuleRepository([]),
            splitter ?? new FakeChapterSplitter([new BookImportChapter(0, 0, "全文", 0, 2)]),
            fileStore ?? new FakeBookFileStore(),
            repository ?? new FakeBookImportRepository(),
            journal ?? new FakeBookOperationJournal(),
            new FakeFileNameMetadataRuleRepository(fileNameRules ??
            [
                new FileNameMetadataRule("default", "默认", @"^(?<name>.+?)\s+作者[:：]\s*(?<author>.+)$", 10, true,
                    DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch)
            ]),
            new FakeTextHeaderMetadataRuleRepository(headerRules ?? []),
            new FakeAppSettingsService(AppSettings.Default with { SplitChaptersOnBlankLines = splitOnBlankLines }),
            new ImportMetadataExtractor(),
            timeProvider ?? TimeProvider.System,
            idGenerator ?? new SequenceBookImportIdGenerator("book-id", "chapter-id"), new BookMutationGate(), sourceChanges ?? new BookSourceChanges());
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

        public string CreateSourceId() => "local-source-id";

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

        public int? FindFirstExplicitTitleOffset(string normalizedText, IReadOnlyList<ChapterRule> rules) => null;

        public IReadOnlyList<BookImportChapter> Split(
            string normalizedText,
            IReadOnlyList<ChapterRule> rules,
            bool splitOnBlankLines) => _chapters;
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
            var path = $"Books/{bookId}/content-{Guid.NewGuid():N}.txt";
            return Task.FromResult(new BookFileCopyHandle(path, path + ".tmp"));
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

    private class FakeBookImportRepository : IBookImportRepository
    {
        public IReadOnlyList<BookImportCandidate> Candidates { get; init; } = [];
        public LocalSourceImportTarget? Target { get; init; }
        public string? MatchedAuthor { get; private set; }
        public Task<IReadOnlyList<BookImportCandidate>> FindCandidatesAsync(string title, string? author, CancellationToken cancellationToken)
        {
            MatchedAuthor = author;
            return Task.FromResult(Candidates);
        }
        public Task<LocalSourceImportTarget?> GetTargetAsync(string bookId, CancellationToken cancellationToken) => Task.FromResult(Target);
        public virtual Task SaveAsync(LocalSourceImportSnapshot snapshot, string operationId, CancellationToken cancellationToken) => Task.CompletedTask;
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

    private sealed class ThrowingBookImportRepository : FakeBookImportRepository
    {
        public override Task SaveAsync(LocalSourceImportSnapshot snapshot, string operationId, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("save failed");
        }
    }

    private sealed class DeferredBookImportRepository : FakeBookImportRepository
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AllowCommit { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override Task SaveAsync(LocalSourceImportSnapshot snapshot, string operationId, CancellationToken cancellationToken)
        {
            Entered.SetResult();
            return AllowCommit.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class CancelingBookImportRepository(CancellationTokenSource cancellation) : FakeBookImportRepository
    {
        public override Task SaveAsync(LocalSourceImportSnapshot snapshot, string operationId, CancellationToken cancellationToken)
        {
            cancellation.Cancel();
            return Task.FromCanceled(cancellationToken);
        }
    }

    private sealed class FakeFileNameMetadataRuleRepository(IReadOnlyList<FileNameMetadataRule> rules) : IFileNameMetadataRuleRepository
    {
        public Task<IReadOnlyList<FileNameMetadataRule>> GetAllAsync(CancellationToken cancellationToken) => Task.FromResult(rules);
        public Task SaveAsync(FileNameMetadataRule rule, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DeleteAsync(string ruleId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SaveOrderAsync(IReadOnlyList<(string RuleId, int SortOrder)> order, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeTextHeaderMetadataRuleRepository(IReadOnlyList<TextHeaderMetadataRule> rules) : ITextHeaderMetadataRuleRepository
    {
        public Task<IReadOnlyList<TextHeaderMetadataRule>> GetAllAsync(CancellationToken cancellationToken) => Task.FromResult(rules);
        public Task SaveAsync(TextHeaderMetadataRule rule, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DeleteAsync(string ruleId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SaveOrderAsync(IReadOnlyList<(string RuleId, int SortOrder)> order, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeAppSettingsService(AppSettings settings) : IAppSettingsService
    {
        public AppSettings Current => settings;
        public event EventHandler<AppSettingsChangedEventArgs>? Changed { add { } remove { } }
        public Task<AppSettings> UpdateAsync(AppSettingsUpdate update, CancellationToken cancellationToken) => Task.FromResult(settings);
    }

    private sealed class CapturingBookImportRepository : FakeBookImportRepository
    {
        public Book? SavedBook => SavedSnapshot?.Book;

        public IReadOnlyList<Chapter>? SavedChapters => SavedSnapshot?.Catalog;
        public LocalSourceImportSnapshot? SavedSnapshot { get; private set; }

        public override Task SaveAsync(LocalSourceImportSnapshot snapshot, string operationId, CancellationToken cancellationToken)
        {
            SavedSnapshot = snapshot;
            return Task.CompletedTask;
        }
    }
}
