using System.Collections.Specialized;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Playback.ActiveCache;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Application.Speech;
using NovelSpeaker.Application.Speech.Rules;
using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.App.Shell.Navigation;
using NovelSpeaker.App.Features.Playback.Scrolling;
using NovelSpeaker.Domain.Books;
using NovelSpeaker.Domain.Settings;
using NovelSpeaker.Domain.Speech;
using NovelSpeaker.TestKit.Common;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Xunit;

namespace NovelSpeaker.App.WpfTests;

public sealed partial class PlayerViewModelTests
{
    private static PlayerViewModel CreateViewModel(
        FakePlaybackCoordinator coordinator,
        IBookPlaybackContentService contentService,
        ITtsRuleQueries? ruleService = null,
        FakeNavigationService? navigationService = null,
        FakeAppFeedbackService? feedbackService = null,
        FakePlayerAutoScrollCoordinator? autoScrollCoordinator = null,
        FakeAppSettingsService? settingsService = null,
        FakeActiveCacheCoordinator? activeCacheCoordinator = null,
        TimeProvider? timeProvider = null)
    {
        return new PlayerViewModel(
            coordinator,
            activeCacheCoordinator ?? new FakeActiveCacheCoordinator(),
            contentService,
            ruleService ?? new FakeTtsRuleQueries([new TtsRuleSummary(1, "默认规则", true, true, null)]),
            settingsService ?? new FakeAppSettingsService(AppSettings.Default),
            feedbackService ?? new FakeAppFeedbackService(),
            navigationService ?? new FakeNavigationService(),
            autoScrollCoordinator ?? new FakePlayerAutoScrollCoordinator(),
            timeProvider ?? TimeProvider.System);
    }

    private sealed class FakeActiveCacheCoordinator : IActiveCacheCoordinator
    {
        private EventHandler<ActiveCacheSnapshot>? _snapshotChanged;

        public ActiveCacheSnapshot? CurrentSnapshot { get; private set; }

        public StartActiveCacheRequest? LastRequest { get; private set; }

        public int CancelCallCount { get; private set; }

        public int SubscriberCount => _snapshotChanged?.GetInvocationList().Length ?? 0;

        public event EventHandler<ActiveCacheSnapshot>? SnapshotChanged
        {
            add => _snapshotChanged += value;
            remove => _snapshotChanged -= value;
        }

        public Task<ActiveCacheStartResult> StartAsync(
            StartActiveCacheRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastRequest = request;
            var snapshot = new ActiveCacheSnapshot(
                Guid.NewGuid(),
                request.BookId,
                "示例小说",
                ActiveCacheBatchStatus.Running,
                0,
                request.ChapterIndices.Count,
                0,
                request.ChapterIndices.Count,
                request.ChapterIndices[0],
                "章节",
                request.ChapterIndices.Select(index => new ActiveCacheChapterSnapshot(
                    index,
                    $"第 {index + 1} 章",
                    0,
                    1,
                    ActiveCacheChapterStatus.Pending,
                    null)).ToArray(),
                null);
            Publish(snapshot);
            return Task.FromResult(new ActiveCacheStartResult(
                ActiveCacheStartStatus.Accepted,
                snapshot.BatchId,
                null));
        }

        public Task CancelAsync(CancellationToken cancellationToken)
        {
            CancelCallCount++;
            return Task.CompletedTask;
        }

        public Task WaitForCurrentBatchAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public void Publish(ActiveCacheSnapshot snapshot)
        {
            CurrentSnapshot = snapshot;
            _snapshotChanged?.Invoke(this, snapshot);
        }
    }

    private sealed class FakePlaybackCoordinator : IPlaybackSession
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

        private readonly TaskCompletionSource _speedChanged = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int OpenPausedCallCount { get; private set; }

        public OpenBookPlaybackRequest? LastOpenPausedRequest { get; private set; }

        public PlaybackStartRequest? LastStartRequest { get; private set; }

        public int? LastJumpedChapterIndex { get; private set; }

        public int? LastJumpedSegmentChapterIndex { get; private set; }

        public int? LastJumpedSegmentIndex { get; private set; }

        public int RetryCurrentSegmentCallCount { get; private set; }

        public Func<PlayerAutoScrollState>? ReadAutoScrollStateDuringSegmentJump { get; set; }

        public PlayerAutoScrollState? AutoScrollStateObservedDuringLastJumpToSegment { get; private set; }

        public int PreviousSegmentCallCount { get; private set; }

        public int NextSegmentCallCount { get; private set; }

        public int PreviousChapterCallCount { get; private set; }

        public int NextChapterCallCount { get; private set; }

        public List<string> OperationLog { get; } = [];

        public event EventHandler<PlaybackSnapshot>? SnapshotChanged;

        public Task StartAsync(PlaybackStartRequest request, CancellationToken cancellationToken)
        {
            LastStartRequest = request;
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
            OperationLog.Add($"JumpToChapter:{chapterIndex}");
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
            OperationLog.Add($"JumpToSegment:{chapterIndex}:{segmentIndex}");
            AutoScrollStateObservedDuringLastJumpToSegment = ReadAutoScrollStateDuringSegmentJump?.Invoke();
            LastJumpedSegmentChapterIndex = chapterIndex;
            LastJumpedSegmentIndex = segmentIndex;
            Publish(CurrentSnapshot with
            {
                ChapterIndex = chapterIndex,
                SegmentIndex = segmentIndex
            });
            return Task.CompletedTask;
        }

        public Task NextSegmentAsync(CancellationToken cancellationToken)
        {
            OperationLog.Add("NextSegment");
            NextSegmentCallCount++;
            Publish(CurrentSnapshot with
            {
                SegmentIndex = CurrentSnapshot.SegmentIndex + 1,
                SegmentCount = Math.Max(CurrentSnapshot.SegmentCount, CurrentSnapshot.SegmentIndex + 2)
            });
            return Task.CompletedTask;
        }

        public Task PreviousSegmentAsync(CancellationToken cancellationToken)
        {
            OperationLog.Add("PreviousSegment");
            PreviousSegmentCallCount++;
            Publish(CurrentSnapshot with
            {
                SegmentIndex = Math.Max(CurrentSnapshot.SegmentIndex - 1, 0)
            });
            return Task.CompletedTask;
        }

        public Task NextChapterAsync(CancellationToken cancellationToken)
        {
            OperationLog.Add("NextChapter");
            NextChapterCallCount++;
            var nextChapterIndex = CurrentSnapshot.ChapterIndex + 1;
            Publish(CurrentSnapshot with
            {
                ChapterIndex = nextChapterIndex,
                ChapterTitle = nextChapterIndex == 0 ? "第一章" : "第二章",
                SegmentIndex = 0,
                SegmentCount = 1
            });
            return Task.CompletedTask;
        }

        public Task PreviousChapterAsync(CancellationToken cancellationToken)
        {
            OperationLog.Add("PreviousChapter");
            PreviousChapterCallCount++;
            var previousChapterIndex = Math.Max(CurrentSnapshot.ChapterIndex - 1, 0);
            Publish(CurrentSnapshot with
            {
                ChapterIndex = previousChapterIndex,
                ChapterTitle = previousChapterIndex == 0 ? "第一章" : "第二章",
                SegmentIndex = 0,
                SegmentCount = 1
            });
            return Task.CompletedTask;
        }
        public Task RetryCurrentSegmentAsync(CancellationToken cancellationToken)
        {
            RetryCurrentSegmentCallCount++;
            return Task.CompletedTask;
        }

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
            _speedChanged.TrySetResult();
            return Task.CompletedTask;
        }

        public Task WaitForSpeedChangeAsync() => _speedChanged.Task.WaitAsync(TimeSpan.FromSeconds(5));

        public Task RefreshBookMetadataAsync(string bookId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task RefreshRegexReplacementAsync(CancellationToken cancellationToken) => Task.CompletedTask;

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

    private sealed class FakePlayerAutoScrollCoordinator : IPlayerAutoScrollCoordinator
    {
        public PlayerAutoScrollState State { get; private set; } = PlayerAutoScrollState.AutoCentering;

        public bool ShouldAutoCenter => State == PlayerAutoScrollState.AutoCentering;

        public bool ShowReturnToCurrentSegment => State != PlayerAutoScrollState.AutoCentering;

        public int PendingRestoreVersion { get; private set; }

        public int ResumeAutoCenterCallCount { get; private set; }

        public List<string> OperationLog { get; } = [];

        public event EventHandler? StateChanged;

        public void NotifyUserScrollInput()
        {
            OperationLog.Add("NotifyUserScrollInput");
            State = PlayerAutoScrollState.ManualBrowsing;
            PendingRestoreVersion++;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void NotifyPassiveScrollChange()
        {
            OperationLog.Add("NotifyPassiveScrollChange");
            NotifyUserScrollInput();
        }

        public void BeginScrollbarDrag()
        {
            OperationLog.Add("BeginScrollbarDrag");
            State = PlayerAutoScrollState.ScrollbarDragging;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void EndScrollbarDrag()
        {
            OperationLog.Add("EndScrollbarDrag");
            State = PlayerAutoScrollState.ManualBrowsing;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void BeginProgrammaticScroll()
        {
        }

        public void EndProgrammaticScroll()
        {
        }

        public void ResumeAutoCenter()
        {
            OperationLog.Add("ResumeAutoCenter");
            ResumeAutoCenterCallCount++;
            State = PlayerAutoScrollState.AutoCentering;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void ResetForPageLeave()
        {
            OperationLog.Add("ResetForPageLeave");
            ResumeAutoCenter();
        }
    }

    private sealed class DelayedBookPlaybackContentService : IBookPlaybackContentService
    {
        private readonly PlaybackBookContent _book;
        private readonly Queue<Task<PlaybackChapterContent?>> _chapterLoads;
        private readonly object _requestSignalSync = new();
        private TaskCompletionSource _requestSignal = CreateRequestSignal();
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
            lock (_requestSignalSync)
            {
                var completedSignal = _requestSignal;
                _requestSignal = CreateRequestSignal();
                completedSignal.TrySetResult();
            }

            return _chapterLoads.Count == 0
                ? Task.FromResult<PlaybackChapterContent?>(null)
                : _chapterLoads.Dequeue();
        }

        public async Task WaitForChapterRequestCountAsync(int expectedCount)
        {
            while (Volatile.Read(ref _chapterRequestCount) < expectedCount)
            {
                Task signal;
                lock (_requestSignalSync)
                {
                    if (Volatile.Read(ref _chapterRequestCount) >= expectedCount)
                    {
                        return;
                    }

                    signal = _requestSignal.Task;
                }

                await signal.WaitAsync(TimeSpan.FromSeconds(5));
            }
        }

        private static TaskCompletionSource CreateRequestSignal() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class FakeTtsRuleQueries : ITtsRuleQueries
    {
        private readonly IReadOnlyList<TtsRuleSummary> _rules;

        public FakeTtsRuleQueries(IReadOnlyList<TtsRuleSummary> rules)
        {
            _rules = rules;
        }

        public Task<IReadOnlyList<TtsRuleSummary>> GetRulesAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(_rules);
        }

        public Task<string?> ExportRuleJsonAsync(long ruleId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeAppSettingsService : IAppSettingsService
    {
        public TaskCompletionSource UpdateStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public FakeAppSettingsService(AppSettings settings)
        {
            Settings = settings;
        }

        public AppSettings Settings { get; private set; }
        public AppSettings Current => Settings;
        public event EventHandler<AppSettingsChangedEventArgs>? Changed { add { } remove { } }

        public Task<AppSettings> UpdateAsync(AppSettingsUpdate update, CancellationToken cancellationToken)
        {
            UpdateStarted.TrySetResult();
            Settings = (Settings with
            {
                DefaultSpeakSpeed = update.DefaultSpeakSpeed ?? Settings.DefaultSpeakSpeed
            }).Normalize();
            return Task.FromResult(Settings);
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

    private sealed class FakeNavigationService : ITestNavigationService
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

}
