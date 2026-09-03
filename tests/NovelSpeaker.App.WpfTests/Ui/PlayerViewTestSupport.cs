using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Automation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Playback.Cache;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Application.Speech;
using NovelSpeaker.Application.Speech.Rules;
using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.App.Shared.Presentation.Controls.Common;
using NovelSpeaker.App.Shared.Presentation.Controls.Feedback;
using NovelSpeaker.App.Shell.Navigation;
using NovelSpeaker.App.Features.Playback.Scrolling;
using NovelSpeaker.Domain.Books;
using NovelSpeaker.Domain.Settings;
using NovelSpeaker.Domain.Speech;
using SymbolIcon = Wpf.Ui.Controls.SymbolIcon;
using SymbolRegular = Wpf.Ui.Controls.SymbolRegular;
using Xunit;

namespace NovelSpeaker.App.WpfTests.Ui;

public sealed partial class PlayerViewTests
{
    private static FrameworkElement? FindDescendantByContent(DependencyObject root, string content)
    {
        return VisualTreeTestHelper.FindDescendant<FrameworkElement>(
            root,
            element => element is ContentControl contentControl &&
                       string.Equals(contentControl.Content as string, content, StringComparison.Ordinal));
    }

    private static FrameworkElement? FindVisibleDescendantByContent(DependencyObject root, string content)
    {
        var element = FindDescendantByContent(root, content);
        if (element is null || !IsEffectivelyVisible(element, root))
        {
            return null;
        }

        return element;
    }

    private static Wpf.Ui.Controls.Button FindUiButtonByAutomationName(DependencyObject root, string name)
    {
        return Assert.IsType<Wpf.Ui.Controls.Button>(VisualTreeTestHelper.FindDescendant<Wpf.Ui.Controls.Button>(
            root,
            button => string.Equals(AutomationProperties.GetName(button), name, StringComparison.Ordinal)));
    }

    private static TextBlock? FindVisibleDescendantByText(DependencyObject root, string text)
    {
        var element = VisualTreeTestHelper.FindDescendant<TextBlock>(
            root,
            textBlock => string.Equals(textBlock.Text, text, StringComparison.Ordinal));
        if (element is null || !IsEffectivelyVisible(element, root))
        {
            return null;
        }

        return element;
    }

    private static bool IsEffectivelyVisible(FrameworkElement element, DependencyObject searchRoot)
    {
        DependencyObject? current = element;
        while (current is not null)
        {
            if (current is UIElement uiElement && uiElement.Visibility != Visibility.Visible)
            {
                return false;
            }

            if (ReferenceEquals(current, searchRoot))
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private static Rect GetBoundsRelativeToRoot(FrameworkElement element, Visual root)
    {
        var origin = element.TransformToAncestor(root).Transform(new Point(0, 0));
        return new Rect(origin.X, origin.Y, element.ActualWidth, element.ActualHeight);
    }

    private static void AssertButtonMetadata(Button button, string expectedName)
    {
        Assert.Equal(expectedName, button.ToolTip);
        Assert.Equal(expectedName, AutomationProperties.GetName(button));
    }

    private static void DoEvents()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static void WaitUntil(Func<bool> predicate, TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            if (predicate())
            {
                return;
            }

            DoEvents();
        }

        DoEvents();
        Assert.True(predicate());
    }

    private static void Pump(TimeSpan duration)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < duration)
        {
            DoEvents();
        }
    }

    private sealed class PlayerViewLayoutTestContext
    {
        public PlayerViewLayoutTestContext(
            ObservableCollection<PlayerChapterItemViewModel> chapters,
            ObservableCollection<PlayerSegmentItemViewModel> segments,
            bool showReturnToCurrentSegment = false,
            bool showPlaybackControls = true,
            bool showNoRuleState = false,
            bool showPlaybackErrorBar = false,
            string errorText = "",
            bool showInlineLoadingState = false,
            string inlineLoadingText = "",
            bool isActiveCacheSelectionMode = false,
            bool canStartActiveCache = false,
            string activeCacheStatusText = "")
        {
            Chapters = chapters;
            Segments = segments;
            CurrentChapterItem = chapters.Count == 0 ? null : chapters.Count > 10 ? chapters[10] : chapters[0];
            CurrentSegmentItem = segments.Count == 0 ? null : segments.Count > 32 ? segments[32] : segments[0];
            ShowReturnToCurrentSegment = showReturnToCurrentSegment;
            ShowPlaybackControls = showPlaybackControls;
            ShowNoRuleState = showNoRuleState;
            ShowPlaybackErrorBar = showPlaybackErrorBar;
            ErrorText = errorText;
            ShowInlineLoadingState = showInlineLoadingState;
            InlineLoadingText = inlineLoadingText;
            IsActiveCacheSelectionMode = isActiveCacheSelectionMode;
            CanStartActiveCache = canStartActiveCache;
            ActiveCacheStatusText = activeCacheStatusText;
            ShowEmptyChapterState = segments.Count == 0 && !showInlineLoadingState && !showNoRuleState;
            CurrentChapterTitle = ShowEmptyChapterState ? "空章节" : "第二章 头铁的落款";
            DisplayedSegmentCounterText = ShowEmptyChapterState ? "0 / 0" : "33 / 140";
            CanTogglePlayPause = !ShowEmptyChapterState;
            CanGoToPreviousSegment = !ShowEmptyChapterState;
            CanGoToNextSegment = !ShowEmptyChapterState;
            SegmentProgressMaximum = ShowEmptyChapterState ? 0d : 139d;
            SegmentProgressValue = ShowEmptyChapterState ? 0d : 32d;
            SegmentProgressPreviewValue = ShowEmptyChapterState ? 0d : 48d;
        }

        public IRelayCommand BackCommand { get; } = new RelayCommand(() => { });

        public IRelayCommand ToggleRuleMenuCommand { get; } = new RelayCommand(() => { });

        public IRelayCommand ToggleStopTimerMenuCommand { get; } = new RelayCommand(() => { });

        public IRelayCommand ToggleSpeedMenuCommand { get; } = new RelayCommand(() => { });

        public IRelayCommand ToggleVolumeMenuCommand { get; } = new RelayCommand(() => { });

        public IRelayCommand ScheduleCustomStopTimerCommand { get; } = new RelayCommand(() => { });

        public IRelayCommand CancelStopTimerCommand { get; } = new RelayCommand(() => { });

        public IRelayCommand EnterActiveCacheSelectionCommand { get; } = new RelayCommand(() => { });

        public IRelayCommand CancelActiveCacheSelectionCommand { get; } = new RelayCommand(() => { });

        public IRelayCommand StartActiveCacheCommand { get; } = new RelayCommand(() => { });

        public IRelayCommand OpenRuleMenuCommand { get; } = new RelayCommand(() => { });

        public IRelayCommand OpenRulesManagementCommand { get; } = new RelayCommand(() => { });

        public IRelayCommand ApplySpeakSpeedCommand { get; } = new RelayCommand(() => { });

        public IRelayCommand IncreaseSpeakSpeedCommand { get; } = new RelayCommand(() => { });

        public IRelayCommand DecreaseSpeakSpeedCommand { get; } = new RelayCommand(() => { });

        public IRelayCommand PreviousChapterCommand { get; } = new RelayCommand(() => { });

        public IRelayCommand PreviousSegmentCommand { get; } = new RelayCommand(() => { });

        public IRelayCommand TogglePlayPauseCommand { get; } = new RelayCommand(() => { });

        public IRelayCommand NextSegmentCommand { get; } = new RelayCommand(() => { });

        public IRelayCommand NextChapterCommand { get; } = new RelayCommand(() => { });

        public IRelayCommand SelectChapterCommand { get; } = new RelayCommand<PlayerChapterItemViewModel?>(_ => { });

        public IRelayCommand SelectSegmentCommand { get; } = new RelayCommand<PlayerSegmentItemViewModel?>(_ => { });

        public IRelayCommand ReturnToCurrentSegmentCommand { get; } = new RelayCommand(() => { });

        public IRelayCommand RetryCurrentSegmentCommand { get; } = new RelayCommand(() => { });

        public string CurrentTitle { get; } = "信息全知者";

        public string CurrentAuthor { get; } = "魔性沧月";

        public string CurrentChapterTitle { get; }

        public string SpeakSpeedButtonText { get; } = "语速 10";

        public string VolumePercentText { get; } = "100%";

        public string VolumeButtonAutomationName { get; } = "播放音量 100%";

        public string StopTimerButtonAutomationName { get; } = "定时停止";

        public string StopTimerRemainingText { get; } = "—";

        public int SpeakSpeed { get; } = 10;

        public double Volume { get; set; } = 1d;

        public SymbolRegular PrimaryActionSymbol { get; } = SymbolRegular.PlayCircle24;

        public string ErrorText { get; }

        public string DisplayedSegmentCounterText { get; }

        public string InlineLoadingText { get; }

        public string ActiveCacheSelectionSummary { get; } = "已选择 2 章";

        public string ActiveCacheStatusText { get; }

        public string SpeedEditorText { get; set; } = "10";

        public string SpeedEditorErrorText { get; } = string.Empty;

        public bool IsRuleMenuOpen { get; set; }

        public bool IsSpeedMenuOpen { get; set; }

        public bool IsStopTimerMenuOpen { get; set; }

        public bool IsVolumeMenuOpen { get; set; }

        public bool ShouldAutoCenterCurrentSegment { get; } = true;

        public bool ShowReturnToCurrentSegment { get; }

        public bool ShowPlaybackControls { get; }

        public bool ShowNoRuleState { get; }

        public bool ShowPlaybackErrorBar { get; }

        public bool ShowEmptyChapterState { get; }

        public bool ShowInlineLoadingState { get; }

        public bool IsActiveCacheSelectionMode { get; }

        public bool CanStartActiveCache { get; }

        public bool HasRules { get; } = false;

        public bool HasAvailableRule { get; } = true;

        public bool CanTogglePlayPause { get; }

        public bool CanDecreaseSpeakSpeed { get; } = true;

        public bool CanIncreaseSpeakSpeed { get; } = true;

        public bool CanScheduleStopTimer { get; } = true;

        public bool HasActiveStopTimer { get; } = false;

        public bool CanGoToPreviousChapter { get; } = true;

        public bool CanGoToNextChapter { get; } = true;

        public bool CanGoToPreviousSegment { get; }

        public bool CanGoToNextSegment { get; }

        public string PrimaryActionText { get; } = "播放";

        public double SegmentProgressMaximum { get; }

        public double SegmentProgressValue { get; }

        public double SegmentProgressPreviewValue { get; }

        public ObservableCollection<PlayerRuleItemViewModel> Rules { get; } = [];

        public ObservableCollection<PlayerChapterItemViewModel> Chapters { get; }

        public ObservableCollection<PlayerSegmentItemViewModel> Segments { get; }

        public PlayerChapterItemViewModel? CurrentChapterItem { get; }

        public PlayerSegmentItemViewModel? CurrentSegmentItem { get; }
    }

    private sealed class FakePlaybackCoordinator : IPlaybackSession
    {
        public FakePlaybackCoordinator(PlaybackSnapshot snapshot)
        {
            CurrentSnapshot = snapshot;
        }

        public PlaybackSnapshot CurrentSnapshot { get; private set; }

        public event EventHandler<PlaybackSnapshot>? SnapshotChanged;

        public void Publish(PlaybackSnapshot snapshot)
        {
            CurrentSnapshot = snapshot;
            SnapshotChanged?.Invoke(this, snapshot);
        }

        public Task StartAsync(PlaybackStartRequest request, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task OpenPausedAsync(OpenBookPlaybackRequest request, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PauseAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ResumeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task JumpToAsync(PlaybackJumpTarget target, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task JumpToChapterAsync(int chapterIndex, CancellationToken cancellationToken)
        {
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

        public Task NextSegmentAsync(CancellationToken cancellationToken)
        {
            Publish(CurrentSnapshot with
            {
                SegmentIndex = CurrentSnapshot.SegmentIndex + 1,
                SegmentCount = Math.Max(CurrentSnapshot.SegmentCount, CurrentSnapshot.SegmentIndex + 2)
            });
            return Task.CompletedTask;
        }

        public Task PreviousSegmentAsync(CancellationToken cancellationToken)
        {
            Publish(CurrentSnapshot with
            {
                SegmentIndex = Math.Max(CurrentSnapshot.SegmentIndex - 1, 0)
            });
            return Task.CompletedTask;
        }

        public Task NextChapterAsync(CancellationToken cancellationToken)
        {
            Publish(CurrentSnapshot with
            {
                ChapterIndex = CurrentSnapshot.ChapterIndex + 1,
                ChapterTitle = "第二章",
                SegmentIndex = 0,
                SegmentCount = 1
            });
            return Task.CompletedTask;
        }

        public Task PreviousChapterAsync(CancellationToken cancellationToken)
        {
            Publish(CurrentSnapshot with
            {
                ChapterIndex = Math.Max(CurrentSnapshot.ChapterIndex - 1, 0),
                ChapterTitle = "第一章",
                SegmentIndex = 0,
                SegmentCount = 1
            });
            return Task.CompletedTask;
        }
        public Task RetryCurrentSegmentAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ChangeRuleAsync(long ruleId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ChangeSpeedAsync(int speakSpeed, CancellationToken cancellationToken) => Task.CompletedTask;
        public void SetVolume(double volume) { }
        public Task RefreshBookMetadataAsync(string bookId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task RefreshRegexReplacementAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task HandleBookDeletedAsync(string bookId, CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
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
            return Task.FromResult(_book);
        }

        public Task<PlaybackChapterContent?> GetChapterAsync(string bookId, int chapterIndex, CancellationToken cancellationToken)
        {
            return Task.FromResult(_chapter);
        }
    }

    private sealed class FakeTtsRuleQueries : ITtsRuleQueries
    {
        private readonly IReadOnlyList<TtsRuleSummary> _rules;

        public FakeTtsRuleQueries(IReadOnlyList<TtsRuleSummary> rules)
        {
            _rules = rules;
        }

        public Task<IReadOnlyList<TtsRuleSummary>> GetRulesAsync(CancellationToken cancellationToken) => Task.FromResult(_rules);
        public Task<string?> ExportRuleJsonAsync(long ruleId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeAppSettingsStore : IAppSettingsService
    {
        public FakeAppSettingsStore(AppSettings settings)
        {
            Settings = settings;
        }

        public AppSettings Settings { get; private set; }
        public AppSettings Current => Settings;
        public event EventHandler<AppSettingsChangedEventArgs>? Changed { add { } remove { } }

        public Task<AppSettings> UpdateAsync(AppSettingsUpdate update, CancellationToken cancellationToken)
        {
            Settings = (Settings with
            {
                DefaultSpeakSpeed = update.DefaultSpeakSpeed ?? Settings.DefaultSpeakSpeed
            }).Normalize();
            return Task.FromResult(Settings);
        }
    }

    private sealed class FakeAppFeedbackService : IAppFeedbackService
    {
        public ProjectedUiError Project(Exception exception)
        {
            return new ProjectedUiError(exception.Message, UiMessageSeverity.Error, false);
        }

        public void ShowProjectedNotification(string title, ProjectedUiError projected)
        {
        }

        public void ShowInformation(string title, string message)
        {
        }

        public void ShowSuccess(string title, string message)
        {
        }

        public void ShowWarning(string title, string message)
        {
        }

        public Task<AppConfirmationDecision> ConfirmDeletionAsync(string title, string message, CancellationToken cancellationToken)
        {
            return Task.FromResult(AppConfirmationDecision.Cancel);
        }
    }

    private sealed class FakeNavigationService : IAppNavigator
    {
        public AppRoute CurrentRoute => AppRoutes.Library;

        public Task<bool> NavigateBackAsync(CancellationToken cancellationToken, bool bypassGuard = false) =>
            Task.FromResult(false);

        public Task<bool> NavigateAsync(AppRoute route, CancellationToken cancellationToken, bool bypassGuard = false) =>
            Task.FromResult(true);
    }

    private sealed class FakePlayerAutoScrollCoordinator : IPlayerAutoScrollCoordinator
    {
        public PlayerAutoScrollState State => PlayerAutoScrollState.AutoCentering;

        public bool ShouldAutoCenter => true;

        public bool ShowReturnToCurrentSegment => false;

        public int PendingRestoreVersion => 0;

        public event EventHandler? StateChanged
        {
            add { }
            remove { }
        }

        public void NotifyUserScrollInput()
        {
        }

        public void NotifyPassiveScrollChange()
        {
        }

        public void BeginScrollbarDrag()
        {
        }

        public void EndScrollbarDrag()
        {
        }

        public void BeginProgrammaticScroll()
        {
        }

        public void EndProgrammaticScroll()
        {
        }

        public void ResumeAutoCenter()
        {
        }

        public void ResetForPageLeave()
        {
        }
    }

    private sealed class FakeCacheWorkspaceService : ICacheWorkspaceService
    {
        public event EventHandler<CacheChangedEventArgs>? Changed;

        public Task<CacheOverviewModel> GetOverviewAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<CachedBookCacheItem>> GetCachedBooksAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<CachedChapterCacheItem>> GetCachedChaptersAsync(
            string bookId,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ChapterCacheStatus>> GetChapterCacheStatusesAsync(
            string bookId,
            IReadOnlyCollection<int> chapterIndices,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ChapterCacheStatus>>([]);

        public Task TrimToConfiguredLimitAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<CacheCleanupResult> ClearBookAsync(string bookId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<CacheCleanupResult> ClearChapterAsync(
            string bookId,
            int chapterIndex,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<CacheCleanupResult> ClearChaptersAsync(
            string bookId,
            IReadOnlyCollection<int> chapterIndices,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<CacheCleanupResult> ClearAllAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public void Publish(CacheChangedEventArgs eventArgs) => Changed?.Invoke(this, eventArgs);
    }
}
