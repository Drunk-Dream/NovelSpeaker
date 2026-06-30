using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Application.Speech;
using NovelSpeaker.App.Navigation;
using NovelSpeaker.App.ViewModels;
using NovelSpeaker.Domain.Settings;
using NovelSpeaker.Domain.Speech;
using Xunit;

namespace NovelSpeaker.UnitTests.ViewModels;

public sealed class PlayerViewModelTests
{
    [Fact]
    public void Constructor_projects_existing_snapshot()
    {
        var coordinator = new FakePlaybackCoordinator(new PlaybackSnapshot(
            PlaybackState.Playing,
            "book-1",
            "示例小说",
            0,
            "第一章 开始",
            1,
            4,
            5,
            "默认规则",
            12,
            200,
            700,
            null,
            false,
            false,
            false));

        var viewModel = new PlayerViewModel(
            coordinator,
            new FakeBookCatalogService([]),
            new FakeTtsRuleLibraryService([]),
            new FakeAppSettingsStore(AppSettings.Default));

        Assert.Equal("示例小说", viewModel.CurrentTitle);
        Assert.Equal("正在播放", viewModel.StatusText);
        Assert.Equal("第 1 章，第 2/4 段 · 在线生成 · 200 / 700 ms", viewModel.DetailText);
        Assert.Equal("暂停", viewModel.PrimaryActionText);
    }

    [Fact]
    public async Task StartSelectedBookCommand_starts_selected_book()
    {
        var coordinator = new FakePlaybackCoordinator();
        var viewModel = new PlayerViewModel(
            coordinator,
            new FakeBookCatalogService([new BookSummary("book-1", "示例小说", null, "第一章 开始", "2026-06-24")]),
            new FakeTtsRuleLibraryService([new TtsRuleSummary(5, "默认规则", true, true, null, TtsRuleCompatibilityStatus.Compatible, [])]),
            new FakeAppSettingsStore(AppSettings.Default));

        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.StartSelectedBookCommand.ExecuteAsync(null);

        Assert.NotNull(coordinator.LastStartRequest);
        Assert.Equal("book-1", coordinator.LastStartRequest!.BookId);
        Assert.Equal(10, coordinator.LastStartRequest.SpeakSpeedOverride);
    }

    [Fact]
    public async Task TogglePlayPauseCommand_pauses_current_playback()
    {
        var coordinator = new FakePlaybackCoordinator(new PlaybackSnapshot(
            PlaybackState.Playing,
            "book-1",
            "示例小说",
            0,
            "第一章 开始",
            0,
            2,
            5,
            "默认规则",
            10,
            0,
            700,
            null,
            false,
            false,
            false));

        var viewModel = new PlayerViewModel(
            coordinator,
            new FakeBookCatalogService([]),
            new FakeTtsRuleLibraryService([]),
            new FakeAppSettingsStore(AppSettings.Default));

        await viewModel.TogglePlayPauseCommand.ExecuteAsync(null);

        Assert.Equal(1, coordinator.PauseCallCount);
    }

    [Fact]
    public async Task ApplySelectedRuleCommand_calls_change_rule_on_coordinator()
    {
        var coordinator = new FakePlaybackCoordinator();
        var viewModel = new PlayerViewModel(
            coordinator,
            new FakeBookCatalogService([]),
            new FakeTtsRuleLibraryService([new TtsRuleSummary(9, "备用规则", true, false, null, TtsRuleCompatibilityStatus.Compatible, [])]),
            new FakeAppSettingsStore(AppSettings.Default));

        await viewModel.LoadAsync(CancellationToken.None);
        viewModel.SelectedRule = viewModel.Rules.Single();

        await viewModel.ApplySelectedRuleCommand.ExecuteAsync(null);

        Assert.Equal(9, coordinator.LastChangedRuleId);
    }

    [Fact]
    public async Task LoadAsync_preserves_current_selections_when_lists_refresh()
    {
        var coordinator = new FakePlaybackCoordinator();
        var catalogService = new MutableBookCatalogService([
            new BookSummary("book-1", "示例小说一", null, "第一章", "2026-06-24"),
            new BookSummary("book-2", "示例小说二", null, "第二章", "2026-06-24")
        ]);
        var ruleService = new MutableTtsRuleLibraryService([
            new TtsRuleSummary(1, "规则一", true, false, null, TtsRuleCompatibilityStatus.Compatible, []),
            new TtsRuleSummary(2, "规则二", true, true, null, TtsRuleCompatibilityStatus.Compatible, [])
        ]);
        var viewModel = new PlayerViewModel(
            coordinator,
            catalogService,
            ruleService,
            new FakeAppSettingsStore(AppSettings.Default));

        await viewModel.LoadAsync(CancellationToken.None);
        viewModel.SelectedBook = viewModel.Books[1];
        viewModel.SelectedRule = viewModel.Rules[1];

        catalogService.Books = [
            new BookSummary("book-2", "示例小说二", null, "第二章", "2026-06-25"),
            new BookSummary("book-1", "示例小说一", null, "第一章", "2026-06-25")
        ];
        ruleService.Rules = [
            new TtsRuleSummary(2, "规则二", true, true, null, TtsRuleCompatibilityStatus.Compatible, []),
            new TtsRuleSummary(1, "规则一", true, false, null, TtsRuleCompatibilityStatus.Compatible, [])
        ];

        await viewModel.LoadAsync(CancellationToken.None);

        Assert.Equal("book-2", viewModel.SelectedBook?.Id);
        Assert.Equal(2, viewModel.SelectedRule?.Id);
    }

    [Fact]
    public void SnapshotChanged_updates_error_projection()
    {
        var coordinator = new FakePlaybackCoordinator();
        var viewModel = new PlayerViewModel(
            coordinator,
            new FakeBookCatalogService([]),
            new FakeTtsRuleLibraryService([]),
            new FakeAppSettingsStore(AppSettings.Default));

        coordinator.Publish(new PlaybackSnapshot(
            PlaybackState.Faulted,
            "book-1",
            "示例小说",
            0,
            "第一章 开始",
            2,
            4,
            5,
            "默认规则",
            10,
            0,
            0,
            "音频解码失败，请重试当前段。",
            false,
            true,
            true));

        Assert.True(viewModel.IsFaulted);
        Assert.True(viewModel.CanRetryCurrentSegment);
        Assert.True(viewModel.CanSkipCurrentSegment);
        Assert.Equal("播放失败", viewModel.StatusText);
        Assert.Equal("音频解码失败，请重试当前段。", viewModel.ErrorText);
        Assert.Equal("音频解码失败，请重试当前段。", viewModel.DetailText);
    }

    [Fact]
    public async Task HandleNavigationAsync_open_paused_calls_coordinator_for_different_book()
    {
        var coordinator = new FakePlaybackCoordinator(new PlaybackSnapshot(
            PlaybackState.Paused,
            "book-1",
            "示例小说",
            0,
            "第一章 开始",
            0,
            2,
            5,
            "默认规则",
            10,
            0,
            700,
            null,
            false,
            false,
            false));
        var viewModel = new PlayerViewModel(
            coordinator,
            new FakeBookCatalogService(
                [
                    new BookSummary("book-1", "示例小说", null, "第一章 开始", "2026-06-24"),
                    new BookSummary("book-2", "另一本书", null, "第二章", "2026-06-24")
                ]),
            new FakeTtsRuleLibraryService([]),
            new FakeAppSettingsStore(AppSettings.Default));

        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.HandleNavigationAsync(
            new PlayerNavigationRequest("book-2", PlayerNavigationMode.OpenPaused),
            CancellationToken.None);

        Assert.Equal(1, coordinator.OpenPausedCallCount);
        Assert.Equal("book-2", coordinator.LastOpenPausedRequest!.BookId);
        Assert.Equal("book-2", viewModel.SelectedBook?.Id);
    }

    [Fact]
    public async Task HandleNavigationAsync_open_paused_keeps_existing_session_for_same_book()
    {
        var coordinator = new FakePlaybackCoordinator(new PlaybackSnapshot(
            PlaybackState.Playing,
            "book-1",
            "示例小说",
            0,
            "第一章 开始",
            0,
            2,
            5,
            "默认规则",
            10,
            120,
            700,
            null,
            false,
            false,
            false));
        var viewModel = new PlayerViewModel(
            coordinator,
            new FakeBookCatalogService([new BookSummary("book-1", "示例小说", null, "第一章 开始", "2026-06-24")]),
            new FakeTtsRuleLibraryService([]),
            new FakeAppSettingsStore(AppSettings.Default));

        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.HandleNavigationAsync(
            new PlayerNavigationRequest("book-1", PlayerNavigationMode.OpenPaused),
            CancellationToken.None);

        Assert.Equal(0, coordinator.OpenPausedCallCount);
        Assert.Equal("book-1", viewModel.SelectedBook?.Id);
        Assert.Equal("示例小说", viewModel.CurrentTitle);
    }

    [Fact]
    public async Task HandleNavigationAsync_return_to_current_session_does_not_reopen_playback()
    {
        var coordinator = new FakePlaybackCoordinator(new PlaybackSnapshot(
            PlaybackState.Playing,
            "book-1",
            "示例小说",
            0,
            "第一章 开始",
            0,
            2,
            5,
            "默认规则",
            10,
            120,
            700,
            null,
            false,
            false,
            false));
        var viewModel = new PlayerViewModel(
            coordinator,
            new FakeBookCatalogService([new BookSummary("book-1", "示例小说", null, "第一章 开始", "2026-06-24")]),
            new FakeTtsRuleLibraryService([]),
            new FakeAppSettingsStore(AppSettings.Default));

        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.HandleNavigationAsync(
            new PlayerNavigationRequest("book-1", PlayerNavigationMode.ReturnToCurrentSession),
            CancellationToken.None);

        Assert.Equal(0, coordinator.OpenPausedCallCount);
        Assert.Equal(PlaybackState.Playing, coordinator.CurrentSnapshot.State);
        Assert.Equal("book-1", viewModel.SelectedBook?.Id);
    }

    private sealed class FakePlaybackCoordinator : IPlaybackCoordinator
    {
        public FakePlaybackCoordinator()
            : this(PlaybackSnapshot.Idle)
        {
        }

        public FakePlaybackCoordinator(PlaybackSnapshot snapshot)
        {
            CurrentSnapshot = snapshot;
        }

        public PlaybackSnapshot CurrentSnapshot { get; private set; }

        public PlaybackStartRequest? LastStartRequest { get; private set; }

        public long? LastChangedRuleId { get; private set; }

        public int PauseCallCount { get; private set; }

        public int OpenPausedCallCount { get; private set; }

        public OpenBookPlaybackRequest? LastOpenPausedRequest { get; private set; }

        public event EventHandler<PlaybackSnapshot>? SnapshotChanged;

        public Task StartAsync(PlaybackStartRequest request, CancellationToken cancellationToken)
        {
            LastStartRequest = request;
            Publish(new PlaybackSnapshot(
                PlaybackState.Playing,
                request.BookId,
                "示例小说",
                request.ChapterIndex ?? 0,
                "第一章 开始",
                request.SegmentIndex ?? 0,
                3,
                5,
                "默认规则",
                request.SpeakSpeedOverride ?? 10,
                request.ResumePositionMilliseconds ?? 0,
                800,
                null,
                false,
                false,
                false));
            return Task.CompletedTask;
        }

        public Task OpenPausedAsync(OpenBookPlaybackRequest request, CancellationToken cancellationToken)
        {
            OpenPausedCallCount++;
            LastOpenPausedRequest = request;
            Publish(CurrentSnapshot with
            {
                State = PlaybackState.Paused,
                BookId = request.BookId,
                ChapterIndex = request.ChapterIndex ?? 0,
                SegmentIndex = request.SegmentIndex ?? 0
            });
            return Task.CompletedTask;
        }

        public Task PauseAsync(CancellationToken cancellationToken)
        {
            PauseCallCount++;
            Publish(CurrentSnapshot with { State = PlaybackState.Paused });
            return Task.CompletedTask;
        }

        public Task ResumeAsync(CancellationToken cancellationToken)
        {
            Publish(CurrentSnapshot with { State = PlaybackState.Playing });
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            Publish(CurrentSnapshot with
            {
                State = PlaybackState.Stopped,
                PositionMilliseconds = 0,
                Message = "已停止当前播放。"
            });
            return Task.CompletedTask;
        }

        public Task JumpToAsync(PlaybackJumpTarget target, CancellationToken cancellationToken)
        {
            Publish(CurrentSnapshot with
            {
                ChapterIndex = target.ChapterIndex,
                SegmentIndex = target.SegmentIndex
            });
            return Task.CompletedTask;
        }

        public Task JumpToChapterAsync(int chapterIndex, CancellationToken cancellationToken)
        {
            Publish(CurrentSnapshot with
            {
                ChapterIndex = chapterIndex,
                SegmentIndex = 0
            });
            return Task.CompletedTask;
        }

        public Task JumpToSegmentAsync(int chapterIndex, int segmentIndex, CancellationToken cancellationToken)
        {
            Publish(CurrentSnapshot with
            {
                ChapterIndex = chapterIndex,
                SegmentIndex = segmentIndex
            });
            return Task.CompletedTask;
        }

        public Task NextSegmentAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PreviousSegmentAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task NextChapterAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PreviousChapterAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task RetryCurrentSegmentAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SkipCurrentSegmentAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ChangeRuleAsync(long ruleId, CancellationToken cancellationToken)
        {
            LastChangedRuleId = ruleId;
            Publish(CurrentSnapshot with { RuleId = ruleId });
            return Task.CompletedTask;
        }

        public Task ChangeSpeedAsync(int speakSpeed, CancellationToken cancellationToken)
        {
            Publish(CurrentSnapshot with { SpeakSpeed = speakSpeed });
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void Publish(PlaybackSnapshot snapshot)
        {
            CurrentSnapshot = snapshot;
            SnapshotChanged?.Invoke(this, snapshot);
        }
    }

    private sealed class FakeBookCatalogService : IBookCatalogService
    {
        private readonly IReadOnlyList<BookSummary> _books;

        public FakeBookCatalogService(IReadOnlyList<BookSummary> books)
        {
            _books = books;
        }

        public Task<IReadOnlyList<BookSummary>> GetBooksAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(_books);
        }
    }

    private sealed class MutableBookCatalogService : IBookCatalogService
    {
        public MutableBookCatalogService(IReadOnlyList<BookSummary> books)
        {
            Books = books;
        }

        public IReadOnlyList<BookSummary> Books { get; set; }

        public Task<IReadOnlyList<BookSummary>> GetBooksAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(Books);
        }
    }

    private sealed class FakeAppSettingsStore : IAppSettingsStore
    {
        public FakeAppSettingsStore(AppSettings settings)
        {
            Settings = settings;
        }

        public AppSettings Settings { get; private set; }

        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(Settings);

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
        {
            Settings = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeTtsRuleLibraryService : ITtsRuleLibraryService
    {
        private readonly IReadOnlyList<TtsRuleSummary> _rules;

        public FakeTtsRuleLibraryService(IReadOnlyList<TtsRuleSummary> rules)
        {
            _rules = rules;
        }

        public Task<IReadOnlyList<TtsRuleSummary>> GetRulesAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(_rules);
        }

        public Task<TtsRuleImportPreview> CreateImportPreviewAsync(string jsonText, string sourceDescription, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<TtsRuleImportResult> ImportAsync(TtsRuleImportPreview preview, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<string?> ExportRuleJsonAsync(long ruleId, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<TtsRuleEditorModel?> GetEditorAsync(long ruleId, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<TtsRuleValidationResult> ValidateEditorAsync(TtsRuleEditorModel editor, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<HttpTtsRule> SaveEditorAsync(TtsRuleEditorModel editor, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<TtsRuleProtectionInfo> GetRuleProtectionAsync(long ruleId, TtsRuleMutationAction action, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<TtsRuleMutationResult> ApplyRuleMutationAsync(TtsRuleMutationDecision decision, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task SelectRuleAsync(long? ruleId, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task SetRuleEnabledAsync(long ruleId, bool isEnabled, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task DeleteRuleAsync(long ruleId, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class MutableTtsRuleLibraryService : ITtsRuleLibraryService
    {
        public MutableTtsRuleLibraryService(IReadOnlyList<TtsRuleSummary> rules)
        {
            Rules = rules;
        }

        public IReadOnlyList<TtsRuleSummary> Rules { get; set; }

        public Task<IReadOnlyList<TtsRuleSummary>> GetRulesAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(Rules);
        }

        public Task<TtsRuleImportPreview> CreateImportPreviewAsync(string jsonText, string sourceDescription, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<TtsRuleImportResult> ImportAsync(TtsRuleImportPreview preview, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<string?> ExportRuleJsonAsync(long ruleId, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<TtsRuleEditorModel?> GetEditorAsync(long ruleId, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<TtsRuleValidationResult> ValidateEditorAsync(TtsRuleEditorModel editor, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<HttpTtsRule> SaveEditorAsync(TtsRuleEditorModel editor, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<TtsRuleProtectionInfo> GetRuleProtectionAsync(long ruleId, TtsRuleMutationAction action, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<TtsRuleMutationResult> ApplyRuleMutationAsync(TtsRuleMutationDecision decision, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task SelectRuleAsync(long? ruleId, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task SetRuleEnabledAsync(long ruleId, bool isEnabled, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task DeleteRuleAsync(long ruleId, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }
}
