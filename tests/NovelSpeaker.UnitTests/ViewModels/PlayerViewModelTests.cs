using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Application.Speech;
using NovelSpeaker.App.Feedback;
using NovelSpeaker.App.Navigation;
using NovelSpeaker.App.Pages;
using NovelSpeaker.App.Player;
using NovelSpeaker.App.ViewModels;
using NovelSpeaker.Domain.Books;
using NovelSpeaker.Domain.Settings;
using NovelSpeaker.Domain.Speech;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Xunit;

namespace NovelSpeaker.UnitTests.ViewModels;

public sealed class PlayerViewModelTests
{
    [Fact]
    public async Task HandleNavigationAsync_open_paused_calls_coordinator_for_different_book_and_loads_projection()
    {
        var coordinator = new FakePlaybackCoordinator(new PlaybackSnapshot(
            PlaybackState.Paused,
            "book-1",
            "示例小说",
            0,
            "第一章",
            0,
            2,
            1,
            "默认规则",
            10,
            0,
            0,
            null,
            false,
            false,
            false,
            "作者甲"));
        var contentService = new FakeBookPlaybackContentService(
            new PlaybackBookContent(
                "book-2",
                "另一本书",
                [new PlaybackChapterContent(0, "第二章", [])],
                "作者乙"),
            new PlaybackChapterContent(
                0,
                "第二章",
                [new SpeechSegment(0, 0, 3, "第二章第一段", "第二章第一段")]));
        var viewModel = CreateViewModel(coordinator, contentService);

        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.HandleNavigationAsync(
            new PlayerNavigationRequest("book-2", PlayerNavigationMode.OpenPaused),
            CancellationToken.None);
        await Task.Delay(20);

        Assert.Equal(1, coordinator.OpenPausedCallCount);
        Assert.Equal("book-2", coordinator.LastOpenPausedRequest!.BookId);
        Assert.Equal("另一本书", viewModel.CurrentTitle);
        Assert.Equal("作者乙", viewModel.CurrentAuthor);
        Assert.Single(viewModel.Chapters);
        Assert.Single(viewModel.Segments);
        Assert.Equal("第二章第一段", viewModel.Segments[0].Text);
    }

    [Fact]
    public async Task HandleNavigationAsync_return_to_current_session_does_not_reopen_playback()
    {
        var coordinator = new FakePlaybackCoordinator(new PlaybackSnapshot(
            PlaybackState.Playing,
            "book-1",
            "示例小说",
            0,
            "第一章",
            1,
            3,
            1,
            "默认规则",
            12,
            0,
            0,
            null,
            false,
            false,
            false,
            "作者甲"));
        var contentService = new FakeBookPlaybackContentService(
            new PlaybackBookContent(
                "book-1",
                "示例小说",
                [new PlaybackChapterContent(0, "第一章", [])],
                "作者甲"),
            new PlaybackChapterContent(
                0,
                "第一章",
                [
                    new SpeechSegment(0, 0, 3, "第一段", "第一段"),
                    new SpeechSegment(1, 3, 3, "第二段", "第二段"),
                    new SpeechSegment(2, 6, 3, "第三段", "第三段")
                ]));
        var viewModel = CreateViewModel(coordinator, contentService);

        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.HandleNavigationAsync(
            new PlayerNavigationRequest("book-1", PlayerNavigationMode.ReturnToCurrentSession),
            CancellationToken.None);

        Assert.Equal(0, coordinator.OpenPausedCallCount);
        Assert.Equal(PlaybackState.Playing, viewModel.CurrentPlaybackState);
        Assert.Equal(1, viewModel.CurrentSegmentIndex);
        Assert.Equal(3, viewModel.CurrentChapterSegmentCount);
        Assert.True(viewModel.Segments[1].IsCurrent);
    }

    [Fact]
    public async Task HandleNavigationAsync_missing_book_navigates_to_library_and_warns()
    {
        var navigationService = new FakeNavigationService();
        var feedbackService = new FakeAppFeedbackService();
        var viewModel = CreateViewModel(
            new FakePlaybackCoordinator(),
            new FakeBookPlaybackContentService(null, null),
            navigationService: navigationService,
            feedbackService: feedbackService);

        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.HandleNavigationAsync(
            new PlayerNavigationRequest("missing-book", PlayerNavigationMode.OpenPaused),
            CancellationToken.None);

        Assert.Equal(typeof(LibraryPage), navigationService.LastNavigationPageType);
        Assert.Equal("无法打开书籍", feedbackService.LastWarningTitle);
    }

    [Fact]
    public async Task SelectChapterCommand_jumps_and_closes_drawer()
    {
        var coordinator = new FakePlaybackCoordinator(new PlaybackSnapshot(
            PlaybackState.Paused,
            "book-1",
            "示例小说",
            0,
            "第一章",
            0,
            2,
            1,
            "默认规则",
            10,
            0,
            0,
            null,
            false,
            false,
            false));
        var layoutController = new FakePlayerLayoutController(isCompactLayout: true, isDrawerOpen: true);
        var viewModel = CreateViewModel(
            coordinator,
            new FakeBookPlaybackContentService(
                new PlaybackBookContent(
                    "book-1",
                    "示例小说",
                    [
                        new PlaybackChapterContent(0, "第一章", []),
                        new PlaybackChapterContent(1, "第二章", [])
                    ],
                    "作者甲"),
                new PlaybackChapterContent(1, "第二章", [new SpeechSegment(0, 0, 4, "第二章第一段", "第二章第一段")])),
            layoutController: layoutController);

        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.HandleNavigationAsync(
            new PlayerNavigationRequest("book-1", PlayerNavigationMode.ReturnToCurrentSession),
            CancellationToken.None);

        await viewModel.SelectChapterCommand.ExecuteAsync(viewModel.Chapters[1]);

        Assert.Equal(1, coordinator.LastJumpedChapterIndex);
        Assert.False(layoutController.IsDrawerOpen);
    }

    [Fact]
    public async Task Snapshot_updates_ignore_stale_chapter_load_results()
    {
        var firstChapterLoad = new TaskCompletionSource<PlaybackChapterContent?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var contentService = new DelayedBookPlaybackContentService(
            new PlaybackBookContent(
                "book-1",
                "示例小说",
                [
                    new PlaybackChapterContent(0, "第一章", []),
                    new PlaybackChapterContent(1, "第二章", [])
                ],
                "作者甲"),
            [
                firstChapterLoad.Task,
                Task.FromResult<PlaybackChapterContent?>(new PlaybackChapterContent(
                    1,
                    "第二章",
                    [new SpeechSegment(0, 0, 4, "第二章第一段", "第二章第一段")]))
            ]);
        var coordinator = new FakePlaybackCoordinator(new PlaybackSnapshot(
            PlaybackState.Paused,
            "book-1",
            "示例小说",
            0,
            "第一章",
            0,
            1,
            1,
            "默认规则",
            10,
            0,
            0,
            null,
            false,
            false,
            false));
        var viewModel = CreateViewModel(coordinator, contentService);

        await viewModel.LoadAsync(CancellationToken.None);
        var navigationTask = viewModel.HandleNavigationAsync(
            new PlayerNavigationRequest("book-1", PlayerNavigationMode.ReturnToCurrentSession),
            CancellationToken.None);

        await contentService.WaitForChapterRequestCountAsync(1);

        coordinator.Publish(coordinator.CurrentSnapshot with
        {
            ChapterIndex = 1,
            ChapterTitle = "第二章",
            SegmentIndex = 0,
            SegmentCount = 1
        });

        await contentService.WaitForChapterRequestCountAsync(2);
        firstChapterLoad.SetResult(new PlaybackChapterContent(
            0,
            "第一章",
            [new SpeechSegment(0, 0, 4, "第一章第一段", "第一章第一段")]));

        await navigationTask;
        await Task.Delay(20);

        Assert.Equal(1, viewModel.CurrentChapterIndex);
        Assert.Single(viewModel.Segments);
        Assert.Equal("第二章第一段", viewModel.Segments[0].Text);
    }

    [Fact]
    public async Task SelectRuleCommand_changes_rule_without_losing_context()
    {
        var coordinator = new FakePlaybackCoordinator(new PlaybackSnapshot(
            PlaybackState.Paused,
            "book-1",
            "示例小说",
            0,
            "第一章",
            0,
            1,
            1,
            "默认规则",
            10,
            0,
            0,
            null,
            false,
            false,
            false));
        var viewModel = CreateViewModel(
            coordinator,
            new FakeBookPlaybackContentService(
                new PlaybackBookContent("book-1", "示例小说", [new PlaybackChapterContent(0, "第一章", [])], "作者甲"),
                new PlaybackChapterContent(0, "第一章", [new SpeechSegment(0, 0, 4, "第一段", "第一段")])),
            ruleService: new FakeTtsRuleLibraryService(
                [
                    new TtsRuleSummary(1, "默认规则", true, true, null, TtsRuleCompatibilityStatus.Compatible, []),
                    new TtsRuleSummary(2, "备用规则", true, false, null, TtsRuleCompatibilityStatus.Compatible, [])
                ]));

        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.HandleNavigationAsync(
            new PlayerNavigationRequest("book-1", PlayerNavigationMode.ReturnToCurrentSession),
            CancellationToken.None);

        await viewModel.SelectRuleCommand.ExecuteAsync(viewModel.Rules[1]);

        Assert.Equal(2, coordinator.LastChangedRuleId);
        Assert.Equal("示例小说", viewModel.CurrentTitle);
    }

    [Fact]
    public async Task ApplySpeakSpeedCommand_changes_speed_with_current_context()
    {
        var coordinator = new FakePlaybackCoordinator(new PlaybackSnapshot(
            PlaybackState.Paused,
            "book-1",
            "示例小说",
            0,
            "第一章",
            0,
            1,
            1,
            "默认规则",
            10,
            0,
            0,
            null,
            false,
            false,
            false));
        var viewModel = CreateViewModel(
            coordinator,
            new FakeBookPlaybackContentService(
                new PlaybackBookContent("book-1", "示例小说", [new PlaybackChapterContent(0, "第一章", [])], "作者甲"),
                new PlaybackChapterContent(0, "第一章", [new SpeechSegment(0, 0, 4, "第一段", "第一段")])));

        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.HandleNavigationAsync(
            new PlayerNavigationRequest("book-1", PlayerNavigationMode.ReturnToCurrentSession),
            CancellationToken.None);

        viewModel.ToggleSpeedMenuCommand.Execute(null);
        viewModel.SpeedEditorText = "18";
        await viewModel.ApplySpeakSpeedCommand.ExecuteAsync(null);

        Assert.Equal(18, coordinator.LastChangedSpeakSpeed);
        Assert.Equal("示例小说", viewModel.CurrentTitle);
    }

    private static PlayerViewModel CreateViewModel(
        FakePlaybackCoordinator coordinator,
        IBookPlaybackContentService contentService,
        ITtsRuleLibraryService? ruleService = null,
        FakeNavigationService? navigationService = null,
        FakeAppFeedbackService? feedbackService = null,
        FakePlayerLayoutController? layoutController = null)
    {
        return new PlayerViewModel(
            coordinator,
            contentService,
            ruleService ?? new FakeTtsRuleLibraryService([new TtsRuleSummary(1, "默认规则", true, true, null, TtsRuleCompatibilityStatus.Compatible, [])]),
            new FakeAppSettingsStore(AppSettings.Default),
            feedbackService ?? new FakeAppFeedbackService(),
            navigationService ?? new FakeNavigationService(),
            layoutController ?? new FakePlayerLayoutController());
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

        public long? LastChangedRuleId { get; private set; }

        public int? LastChangedSpeakSpeed { get; private set; }

        public int OpenPausedCallCount { get; private set; }

        public OpenBookPlaybackRequest? LastOpenPausedRequest { get; private set; }

        public int? LastJumpedChapterIndex { get; private set; }

        public event EventHandler<PlaybackSnapshot>? SnapshotChanged;

        public Task StartAsync(PlaybackStartRequest request, CancellationToken cancellationToken)
        {
            Publish(CurrentSnapshot with
            {
                State = PlaybackState.Playing,
                BookId = request.BookId,
                ChapterIndex = request.ChapterIndex ?? CurrentSnapshot.ChapterIndex,
                SegmentIndex = request.SegmentIndex ?? CurrentSnapshot.SegmentIndex,
                SpeakSpeed = request.SpeakSpeedOverride ?? CurrentSnapshot.SpeakSpeed
            });
            return Task.CompletedTask;
        }

        public Task OpenPausedAsync(OpenBookPlaybackRequest request, CancellationToken cancellationToken)
        {
            OpenPausedCallCount++;
            LastOpenPausedRequest = request;
            Publish(new PlaybackSnapshot(
                PlaybackState.Paused,
                request.BookId,
                request.BookId == "book-2" ? "另一本书" : "示例小说",
                request.ChapterIndex ?? 0,
                request.BookId == "book-2" ? "第二章" : "第一章",
                request.SegmentIndex ?? 0,
                1,
                1,
                "默认规则",
                request.SpeakSpeedOverride ?? 10,
                0,
                0,
                null,
                false,
                false,
                false,
                request.BookId == "book-2" ? "作者乙" : "作者甲"));
            return Task.CompletedTask;
        }

        public Task PauseAsync(CancellationToken cancellationToken)
        {
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
            Publish(CurrentSnapshot with { State = PlaybackState.Stopped });
            return Task.CompletedTask;
        }

        public Task JumpToAsync(PlaybackJumpTarget target, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task JumpToChapterAsync(int chapterIndex, CancellationToken cancellationToken)
        {
            LastJumpedChapterIndex = chapterIndex;
            Publish(CurrentSnapshot with
            {
                ChapterIndex = chapterIndex,
                ChapterTitle = chapterIndex == 0 ? "第一章" : "第二章",
                SegmentIndex = 0,
                SegmentCount = 1
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
            LastChangedSpeakSpeed = speakSpeed;
            Publish(CurrentSnapshot with { SpeakSpeed = speakSpeed });
            return Task.CompletedTask;
        }

        public Task RefreshBookMetadataAsync(string bookId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task HandleBookDeletedAsync(string bookId, CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void Publish(PlaybackSnapshot snapshot)
        {
            CurrentSnapshot = snapshot;
            SnapshotChanged?.Invoke(this, snapshot);
        }
    }

    private sealed class FakeBookPlaybackContentService : IBookPlaybackContentService
    {
        private readonly PlaybackBookContent? _book;
        private readonly PlaybackChapterContent? _chapter;

        public FakeBookPlaybackContentService(PlaybackBookContent? book, PlaybackChapterContent? chapter)
        {
            _book = book;
            _chapter = chapter;
        }

        public Task<PlaybackBookContent?> GetBookAsync(string bookId, CancellationToken cancellationToken)
        {
            if (_book is null || !string.Equals(_book.BookId, bookId, StringComparison.Ordinal))
            {
                return Task.FromResult<PlaybackBookContent?>(null);
            }

            return Task.FromResult<PlaybackBookContent?>(_book);
        }

        public Task<PlaybackChapterContent?> GetChapterAsync(string bookId, int chapterIndex, CancellationToken cancellationToken)
        {
            if (_book is null || _chapter is null ||
                !string.Equals(_book.BookId, bookId, StringComparison.Ordinal) ||
                _chapter.ChapterIndex != chapterIndex)
            {
                return Task.FromResult<PlaybackChapterContent?>(null);
            }

            return Task.FromResult<PlaybackChapterContent?>(_chapter);
        }
    }

    private sealed class DelayedBookPlaybackContentService : IBookPlaybackContentService
    {
        private readonly PlaybackBookContent _book;
        private readonly Queue<Task<PlaybackChapterContent?>> _chapterLoads;
        private int _chapterRequestCount;

        public DelayedBookPlaybackContentService(
            PlaybackBookContent book,
            IEnumerable<Task<PlaybackChapterContent?>> chapterLoads)
        {
            _book = book;
            _chapterLoads = new Queue<Task<PlaybackChapterContent?>>(chapterLoads);
        }

        public Task<PlaybackBookContent?> GetBookAsync(string bookId, CancellationToken cancellationToken)
        {
            return Task.FromResult<PlaybackBookContent?>(_book);
        }

        public Task<PlaybackChapterContent?> GetChapterAsync(string bookId, int chapterIndex, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _chapterRequestCount);
            return _chapterLoads.Count == 0
                ? Task.FromResult<PlaybackChapterContent?>(null)
                : _chapterLoads.Dequeue();
        }

        public async Task WaitForChapterRequestCountAsync(int expectedCount)
        {
            while (Volatile.Read(ref _chapterRequestCount) < expectedCount)
            {
                await Task.Delay(10);
            }
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

        public Task<TtsRuleImportPreview> CreateImportPreviewAsync(string jsonText, string sourceDescription, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<TtsRuleImportResult> ImportAsync(TtsRuleImportPreview preview, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string?> ExportRuleJsonAsync(long ruleId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<TtsRuleEditorModel?> GetEditorAsync(long ruleId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<TtsRuleValidationResult> ValidateEditorAsync(TtsRuleEditorModel editor, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<HttpTtsRule> SaveEditorAsync(TtsRuleEditorModel editor, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<TtsRuleProtectionInfo> GetRuleProtectionAsync(long ruleId, TtsRuleMutationAction action, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<TtsRuleMutationResult> ApplyRuleMutationAsync(TtsRuleMutationDecision decision, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SelectRuleAsync(long? ruleId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SetRuleEnabledAsync(long ruleId, bool isEnabled, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteRuleAsync(long ruleId, CancellationToken cancellationToken) => throw new NotSupportedException();
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

    private sealed class FakeAppFeedbackService : IAppFeedbackService
    {
        public string? LastWarningTitle { get; private set; }

        public ProjectedUiError Project(Exception exception) => new(exception.Message, UiMessageSeverity.Error, false);

        public void ShowProjectedNotification(string title, ProjectedUiError projected)
        {
        }

        public void ShowSuccess(string title, string message)
        {
        }

        public void ShowWarning(string title, string message)
        {
            LastWarningTitle = title;
        }

        public Task<AppConfirmationDecision> ConfirmDeletionAsync(string title, string message, CancellationToken cancellationToken)
        {
            return Task.FromResult(AppConfirmationDecision.Cancel);
        }
    }

    private sealed class FakeNavigationService : INavigationService
    {
        public Type? LastNavigationPageType { get; private set; }

        public object? LastNavigationData { get; private set; }

        public INavigationView GetNavigationControl() => throw new NotSupportedException();

        public bool GoBack() => false;

        public bool Navigate(Type pageType) => true;

        public bool Navigate(Type pageType, object? dataContext) => true;

        public bool Navigate(string pageIdOrTargetTag) => true;

        public bool Navigate(string pageIdOrTargetTag, object? dataContext) => true;

        public bool NavigateWithHierarchy(Type pageType)
        {
            LastNavigationPageType = pageType;
            LastNavigationData = null;
            return true;
        }

        public bool NavigateWithHierarchy(Type pageType, object? dataContext)
        {
            LastNavigationPageType = pageType;
            LastNavigationData = dataContext;
            return true;
        }

        public void SetNavigationControl(INavigationView navigation)
        {
        }
    }

    private sealed class FakePlayerLayoutController : IPlayerLayoutController
    {
        public FakePlayerLayoutController(bool isCompactLayout = false, bool isDrawerOpen = false)
        {
            IsCompactLayout = isCompactLayout;
            IsDrawerOpen = isDrawerOpen;
        }

        public bool IsCompactLayout { get; private set; }

        public bool IsDrawerOpen { get; private set; }

        public event EventHandler? StateChanged;

        public void UpdateWidth(double width)
        {
            var nextIsCompact = width < 1080d;
            if (nextIsCompact == IsCompactLayout)
            {
                return;
            }

            IsCompactLayout = nextIsCompact;
            if (!IsCompactLayout)
            {
                IsDrawerOpen = false;
            }

            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void OpenDrawer()
        {
            IsDrawerOpen = true;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void CloseDrawer()
        {
            IsDrawerOpen = false;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void ToggleDrawer()
        {
            IsDrawerOpen = !IsDrawerOpen;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
