using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Playback.ActiveCache;
using NovelSpeaker.Application.Playback.Cache;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Application.Speech.Rules;
using NovelSpeaker.App.Desktop.MiniPlayer;
using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.App.Shared.Presentation;
using NovelSpeaker.App.Shared.Presentation.Cache;
using NovelSpeaker.App.Shared.Presentation.Platform;
using NovelSpeaker.App.Shared.Presentation.Selection;
using NovelSpeaker.App.Shell.Navigation;
using NovelSpeaker.App.Features.Playback.Components;
using NovelSpeaker.App.Features.Playback.Scrolling;
using NovelSpeaker.Domain.Settings;

namespace NovelSpeaker.App.Features.Playback.Presentation;

public sealed partial class PlayerViewModel : ObservableObject, ISegmentProgressInteractionTarget
{
    private readonly IPlaybackSession _playbackCoordinator;
    private readonly IPlaybackStopTimer _stopTimer;
    private readonly IActiveCacheCoordinator _activeCacheCoordinator;
    private readonly ICacheWorkspaceService _cacheWorkspaceService;
    private readonly IAppSettingsService _settingsService;
    private readonly IAppNavigator _navigator;
    private readonly IPlayerAutoScrollCoordinator _autoScrollCoordinator;
    private readonly IUiScheduler _uiScheduler;
    private readonly IAppFeedbackService _feedbackService;
    private readonly IMiniPlayerLauncher _miniPlayerLauncher;
    private readonly TimeProvider _timeProvider;
    private readonly PlayerContentProjection _contentProjection;
    private readonly PlayerSnapshotProjection _snapshotProjection;
    private readonly PlayerRulesAndSpeedController _rulesAndSpeedController;
    private readonly PlayerActiveCacheSelectionController _activeCacheSelection;
    private readonly ChapterCacheStatusRefreshController _cacheStatusRefresh;
    private readonly OwnedTaskRegistry _pageTasks = new();

    private string? _requestedBookId;
    private int _segmentCenterRequestVersion;
    private bool _animateNextSegmentCenterRequest;
    private bool _suppressNextStateDrivenAutoCenterRequest;
    private PlayerAutoScrollState _lastAppliedAutoScrollState;
    private PlaybackSnapshot _lastAppliedSnapshot = PlaybackSnapshot.Idle;
    private long _lastAppliedStopTimerVersion = -1;
    private ITimer? _stopTimerDisplayTimer;
    private CancellationTokenSource _pageEventCancellation = new();
    private bool _isPageEventsRegistered;
    private string? _cacheStatusInitializedBookId;

    public PlayerViewModel(
        IPlaybackSession playbackCoordinator,
        IPlaybackStopTimer stopTimer,
        IActiveCacheCoordinator activeCacheCoordinator,
        IBookPlaybackContentService bookPlaybackContentService,
        ITtsRuleQueries ruleQueries,
        IAppSettingsService settingsService,
        IAppFeedbackService feedbackService,
        IAppNavigator navigator,
        IPlayerAutoScrollCoordinator autoScrollCoordinator,
        ICacheWorkspaceService cacheWorkspaceService,
        IMiniPlayerLauncher miniPlayerLauncher,
        TimeProvider? timeProvider = null,
        IUiScheduler? uiScheduler = null)
    {
        _playbackCoordinator = playbackCoordinator;
        _stopTimer = stopTimer ?? throw new ArgumentNullException(nameof(stopTimer));
        _activeCacheCoordinator = activeCacheCoordinator;
        _cacheWorkspaceService = cacheWorkspaceService;
        _settingsService = settingsService;
        _feedbackService = feedbackService;
        _miniPlayerLauncher = miniPlayerLauncher ?? throw new ArgumentNullException(nameof(miniPlayerLauncher));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _navigator = navigator;
        _autoScrollCoordinator = autoScrollCoordinator;
        _uiScheduler = uiScheduler ?? new WpfUiScheduler();
        _contentProjection = new PlayerContentProjection(bookPlaybackContentService);
        _snapshotProjection = new PlayerSnapshotProjection();
        _rulesAndSpeedController = new PlayerRulesAndSpeedController(
            playbackCoordinator,
            ruleQueries,
            settingsService,
            feedbackService,
            _timeProvider);
        _activeCacheSelection = new PlayerActiveCacheSelectionController(_activeCacheCoordinator);
        _activeCacheSelection.StateChanged += OnActiveCacheSelectionStateChanged;
        _cacheStatusRefresh = new ChapterCacheStatusRefreshController(
            _cacheWorkspaceService,
            _uiScheduler,
            ApplyChapterCacheStatuses,
            exception => ReportViewOperationFailure("刷新章节缓存进度失败", exception));
        _cacheStatusRefresh.Activate(_pageEventCancellation.Token);
        _lastAppliedAutoScrollState = _autoScrollCoordinator.State;

        ApplyAutoScrollState();
        ApplySnapshot(_playbackCoordinator.CurrentSnapshot);
        ApplyStopTimerSnapshot(_stopTimer.CurrentSnapshot);

        RegisterPageEvents();
    }

    public ObservableCollection<PlayerRuleItemViewModel> Rules { get; } = [];

    public ObservableCollection<PlayerChapterItemViewModel> Chapters => _contentProjection.Chapters;

    public ObservableCollection<PlayerSegmentItemViewModel> Segments => _contentProjection.Segments;

    public bool HasRules => Rules.Count > 0;

    public bool ShowPlaybackControls => HasAvailableRule;

    public bool ShowNoRuleState => !HasAvailableRule;

    public bool ShowPlaybackErrorBar => IsFaulted && !string.IsNullOrWhiteSpace(ErrorText);

    public bool CanTogglePlayPause => HasAvailableRule && !IsFaulted;

    public bool CanDecreaseSpeakSpeed => SpeakSpeed > AppSettings.MinSpeakSpeed;

    public bool CanIncreaseSpeakSpeed => SpeakSpeed < AppSettings.MaxSpeakSpeed;

    public bool IsActiveCacheSelectionMode => _activeCacheSelection.IsSelectionMode;

    public int SelectedActiveCacheChapterCount => _activeCacheSelection.SelectedChapterCount;

    public string ActiveCacheSelectionSummary => _activeCacheSelection.SelectionSummary;

    public string ActiveCacheStatusText => _activeCacheSelection.StatusText;

    public bool HasActiveCacheBatch => _activeCacheSelection.HasActiveBatch;

    public bool CanStartActiveCache => _activeCacheSelection.CanStart;

    public bool CanScheduleStopTimer =>
        !string.IsNullOrWhiteSpace(_playbackCoordinator.CurrentSnapshot.BookId) &&
        CurrentPlaybackState is PlaybackState.Playing or PlaybackState.Paused;

    public string SpeakSpeedButtonText => $"语速 {SpeakSpeed}";

    public string StopTimerButtonAutomationName => HasActiveStopTimer
        ? $"定时停止，剩余 {StopTimerRemainingText}"
        : "定时停止";

    public PlaybackPrimaryAction PrimaryAction => CurrentPlaybackState == PlaybackState.Playing
        ? PlaybackPrimaryAction.Pause
        : PlaybackPrimaryAction.Play;

    public string DisplayedSegmentCounterText => BuildSegmentCounterText(
        IsSegmentProgressDragging ? (int)Math.Round(SegmentProgressPreviewValue) : CurrentSegmentIndex,
        CurrentChapterSegmentCount);

    public PlayerAutoScrollState AutoScrollState => _autoScrollCoordinator.State;

    public bool ShouldAutoCenterCurrentSegment => _autoScrollCoordinator.ShouldAutoCenter;

    public bool ShowInlineLoadingState => CurrentPlaybackState is PlaybackState.Preparing or PlaybackState.Buffering or PlaybackState.Recovering;

    public string InlineLoadingText => CurrentPlaybackState switch
    {
        PlaybackState.Preparing => "正在准备",
        PlaybackState.Buffering => "正在加载",
        PlaybackState.Recovering => "正在恢复",
        _ => string.Empty
    };

    public double SegmentProgressMaximum => Math.Max(CurrentChapterSegmentCount - 1, 0);

    public int SegmentCenterRequestVersion => _segmentCenterRequestVersion;

    public bool AnimateNextSegmentCenterRequest => _animateNextSegmentCenterRequest;

    [ObservableProperty]
    private string currentTitle = "未打开书籍";

    [ObservableProperty]
    private string currentAuthor = "未知作者";

    [ObservableProperty]
    private string currentChapterTitle = "尚未定位章节";

    [ObservableProperty]
    private string errorText = string.Empty;

    [ObservableProperty]
    private string primaryActionText = "播放";

    [ObservableProperty]
    private bool isFaulted;

    [ObservableProperty]
    private bool hasAvailableRule = true;

    [ObservableProperty]
    private PlaybackState currentPlaybackState = PlaybackState.Idle;

    [ObservableProperty]
    private int speakSpeed = AppSettings.DefaultSpeakSpeedValue;

    [ObservableProperty]
    private int currentChapterIndex = -1;

    [ObservableProperty]
    private int currentSegmentIndex = -1;

    [ObservableProperty]
    private int currentChapterSegmentCount;

    [ObservableProperty]
    private bool canGoToPreviousChapter;

    [ObservableProperty]
    private bool canGoToNextChapter;

    [ObservableProperty]
    private bool canGoToPreviousSegment;

    [ObservableProperty]
    private bool canGoToNextSegment;

    [ObservableProperty]
    private bool isRuleMenuOpen;

    [ObservableProperty]
    private bool isSpeedMenuOpen;

    [ObservableProperty]
    private bool isStopTimerMenuOpen;

    [ObservableProperty]
    private string customStopMinutesText = string.Empty;

    [ObservableProperty]
    private string customStopTimerErrorText = string.Empty;

    [ObservableProperty]
    private string stopTimerRemainingText = "—";

    [ObservableProperty]
    private bool hasActiveStopTimer;

    [ObservableProperty]
    private string speedEditorText = AppSettings.DefaultSpeakSpeedValue.ToString(CultureInfo.InvariantCulture);

    [ObservableProperty]
    private string speedEditorErrorText = string.Empty;

    [ObservableProperty]
    private PlayerChapterItemViewModel? currentChapterItem;

    [ObservableProperty]
    private PlayerSegmentItemViewModel? currentSegmentItem;

    [ObservableProperty]
    private bool showReturnToCurrentSegment;

    [ObservableProperty]
    private double segmentProgressValue;

    [ObservableProperty]
    private double segmentProgressPreviewValue;

    [ObservableProperty]
    private bool isSegmentProgressDragging;

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _rulesAndSpeedController.RefreshDefaultSpeakSpeed();

        if (string.IsNullOrWhiteSpace(_playbackCoordinator.CurrentSnapshot.BookId) ||
            !AppSettings.IsValidSpeakSpeed(_playbackCoordinator.CurrentSnapshot.SpeakSpeed))
        {
            SpeakSpeed = _rulesAndSpeedController.DefaultSpeakSpeed;
        }

        await RefreshRulesAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        ApplySnapshot(_playbackCoordinator.CurrentSnapshot);
    }

    public async Task HandleNavigationAsync(PlayerRoute request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        _requestedBookId = request.BookId;
        _activeCacheSelection.ExitSelectionMode();
        CloseTransientPanels();
        ResumeAutoCenterForExplicitNavigation();

        var book = await EnsureBookLoadedAsync(request.BookId, cancellationToken);
        if (book is null)
        {
            await HandleMissingBookAsync(cancellationToken);
            return;
        }

        var snapshot = _playbackCoordinator.CurrentSnapshot;
        if (request.ChapterIndex is not null)
        {
            await HandleChapterTargetNavigationAsync(request, snapshot, cancellationToken);
            return;
        }

        if (request.Mode == PlayerNavigationMode.ReturnToCurrentSession ||
            string.Equals(snapshot.BookId, request.BookId, StringComparison.Ordinal))
        {
            ApplySnapshot(snapshot);
            await EnsureContentLoadedForSnapshotAsync(snapshot, cancellationToken);
            await RestoreMissingRuleSessionAsync(request.BookId, snapshot, cancellationToken);
            return;
        }

        await _playbackCoordinator.OpenPausedAsync(
            new OpenBookPlaybackRequest(
                request.BookId,
                null,
                null,
                ResolveSpeakSpeedForOpen()),
            cancellationToken);

        snapshot = _playbackCoordinator.CurrentSnapshot;
        ApplySnapshot(snapshot);
        await EnsureContentLoadedForSnapshotAsync(snapshot, cancellationToken);
    }

    private async Task HandleChapterTargetNavigationAsync(
        PlayerRoute request,
        PlaybackSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var targetChapterIndex = request.ChapterIndex.GetValueOrDefault();
        var targetSegmentIndex = request.SegmentIndex ?? 0;
        var isCurrentBook = string.Equals(snapshot.BookId, request.BookId, StringComparison.Ordinal);

        if (isCurrentBook)
        {
            if (request.SegmentIndex is not null && targetSegmentIndex > 0)
            {
                await _playbackCoordinator.JumpToSegmentAsync(targetChapterIndex, targetSegmentIndex, cancellationToken);
            }
            else
            {
                await _playbackCoordinator.JumpToChapterAsync(targetChapterIndex, cancellationToken);
            }

            snapshot = _playbackCoordinator.CurrentSnapshot;
            ApplySnapshot(snapshot);
            await EnsureContentLoadedForSnapshotAsync(snapshot, cancellationToken);
            return;
        }

        if (snapshot.State == PlaybackState.Playing)
        {
            await _playbackCoordinator.StartAsync(
                new PlaybackStartRequest(
                    request.BookId,
                    targetChapterIndex,
                    targetSegmentIndex,
                    null,
                    ResolveSpeakSpeedForOpen()),
                cancellationToken);
        }
        else
        {
            await _playbackCoordinator.OpenPausedAsync(
                new OpenBookPlaybackRequest(
                    request.BookId,
                    targetChapterIndex,
                    targetSegmentIndex,
                    ResolveSpeakSpeedForOpen()),
                cancellationToken);
        }

        snapshot = _playbackCoordinator.CurrentSnapshot;
        ApplySnapshot(snapshot);
        await EnsureContentLoadedForSnapshotAsync(snapshot, cancellationToken);
    }

    public void OnPageNavigatedFrom()
    {
        _pageEventCancellation.Cancel();
        StopStopTimerDisplayTimer();
        _cacheStatusRefresh.Deactivate();
        _rulesAndSpeedController.CancelPendingSpeakSpeedChange();
        CloseTransientPanels();
        _autoScrollCoordinator.ResetForPageLeave();
        _activeCacheSelection.ExitSelectionMode();
        if (!_isPageEventsRegistered)
        {
            return;
        }

        _playbackCoordinator.SnapshotChanged -= OnSnapshotChanged;
        _stopTimer.SnapshotChanged -= OnStopTimerSnapshotChanged;
        _activeCacheCoordinator.SnapshotChanged -= OnActiveCacheSnapshotChanged;
        _cacheWorkspaceService.Changed -= OnCacheChanged;
        _settingsService.Changed -= OnSettingsChanged;
        _autoScrollCoordinator.StateChanged -= OnAutoScrollStateChanged;
        _isPageEventsRegistered = false;
    }

    public void OnPageNavigatedTo(CancellationToken cancellationToken)
    {
        _pageEventCancellation.Dispose();
        _pageEventCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _cacheStatusRefresh.Activate(_pageEventCancellation.Token);
        RegisterPageEvents();
        ApplySnapshot(_playbackCoordinator.CurrentSnapshot);
        var stopTimerSnapshot = _stopTimer.CurrentSnapshot;
        ApplyStopTimerSnapshot(stopTimerSnapshot);
        RefreshStopTimerDisplay();
        UpdateStopTimerDisplayTimer(stopTimerSnapshot.IsActive);
        _activeCacheSelection.ApplySnapshot(_activeCacheCoordinator.CurrentSnapshot);
        QueueCacheStatusRefresh(chapterIndex: null);
    }

    private void RegisterPageEvents()
    {
        if (_isPageEventsRegistered)
        {
            return;
        }

        _playbackCoordinator.SnapshotChanged += OnSnapshotChanged;
        _stopTimer.SnapshotChanged += OnStopTimerSnapshotChanged;
        _activeCacheCoordinator.SnapshotChanged += OnActiveCacheSnapshotChanged;
        _cacheWorkspaceService.Changed += OnCacheChanged;
        _settingsService.Changed += OnSettingsChanged;
        _autoScrollCoordinator.StateChanged += OnAutoScrollStateChanged;
        _isPageEventsRegistered = true;
    }

    public void NotifyUserScrollInput()
    {
        _autoScrollCoordinator.NotifyUserScrollInput();
    }

    public void NotifyPassiveSegmentScrollChange()
    {
        _autoScrollCoordinator.NotifyPassiveScrollChange();
    }

    public void NotifyScrollbarDragStarted()
    {
        _autoScrollCoordinator.BeginScrollbarDrag();
    }

    public void NotifyScrollbarDragCompleted()
    {
        _autoScrollCoordinator.EndScrollbarDrag();
    }

    public void NotifyProgrammaticScrollStarted()
    {
        _autoScrollCoordinator.BeginProgrammaticScroll();
    }

    public void NotifyProgrammaticScrollCompleted()
    {
        _autoScrollCoordinator.EndProgrammaticScroll();
    }

    public void BeginSegmentProgressInteraction()
    {
        if (CurrentChapterSegmentCount <= 0)
        {
            return;
        }

        IsSegmentProgressDragging = true;
        SegmentProgressPreviewValue = SegmentProgressValue;
        OnPropertyChanged(nameof(DisplayedSegmentCounterText));
    }

    public void PreviewSegmentProgress(double value)
    {
        if (!IsSegmentProgressDragging)
        {
            return;
        }

        SegmentProgressPreviewValue = NormalizeSegmentProgressValue(value);
        OnPropertyChanged(nameof(DisplayedSegmentCounterText));
    }

    public async Task CommitSegmentProgressAsync(double value, CancellationToken cancellationToken)
    {
        if (CurrentChapterSegmentCount <= 0 || CurrentChapterIndex < 0)
        {
            CancelSegmentProgressInteraction();
            return;
        }

        var targetSegmentIndex = (int)Math.Round(NormalizeSegmentProgressValue(value));
        IsSegmentProgressDragging = false;
        SegmentProgressPreviewValue = targetSegmentIndex;
        OnPropertyChanged(nameof(DisplayedSegmentCounterText));

        if (targetSegmentIndex == CurrentSegmentIndex)
        {
            SegmentProgressValue = targetSegmentIndex;
            ResumeAutoCenterAndRequest(animate: true);
            return;
        }

        ResumeAutoCenterForExplicitNavigation();
        await _playbackCoordinator.JumpToSegmentAsync(CurrentChapterIndex, targetSegmentIndex, cancellationToken);
    }

    public void CancelSegmentProgressInteraction()
    {
        if (!IsSegmentProgressDragging)
        {
            return;
        }

        IsSegmentProgressDragging = false;
        SegmentProgressPreviewValue = SegmentProgressValue;
        OnPropertyChanged(nameof(DisplayedSegmentCounterText));
    }

    internal void ReportViewOperationFailure(string title, Exception exception)
    {
        var projected = _feedbackService.Project(exception);
        _feedbackService.ShowProjectedNotification(title, projected);
    }

    [RelayCommand]
    private async Task BackAsync(CancellationToken cancellationToken)
    {
        if (!await _navigator.GoBackAsync(cancellationToken).ConfigureAwait(true))
        {
            await _navigator.NavigateAsync(AppRoutes.Library, cancellationToken).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private void ToggleRuleMenu()
    {
        IsSpeedMenuOpen = false;
        IsStopTimerMenuOpen = false;
        IsRuleMenuOpen = !IsRuleMenuOpen;
    }

    [RelayCommand]
    private void ToggleSpeedMenu()
    {
        IsRuleMenuOpen = false;
        IsStopTimerMenuOpen = false;
        if (!IsSpeedMenuOpen)
        {
            SpeedEditorText = SpeakSpeed.ToString(CultureInfo.InvariantCulture);
            SpeedEditorErrorText = string.Empty;
        }

        IsSpeedMenuOpen = !IsSpeedMenuOpen;
    }

    [RelayCommand]
    private void OpenRuleMenu()
    {
        IsSpeedMenuOpen = false;
        IsStopTimerMenuOpen = false;
        IsRuleMenuOpen = true;
    }

    [RelayCommand]
    private void ToggleStopTimerMenu()
    {
        IsRuleMenuOpen = false;
        IsSpeedMenuOpen = false;
        CustomStopTimerErrorText = string.Empty;
        IsStopTimerMenuOpen = !IsStopTimerMenuOpen;
    }

    [RelayCommand]
    private void ScheduleStopAfter15Minutes() => ScheduleStopAfterMinutes(15);

    [RelayCommand]
    private void ScheduleStopAfter30Minutes() => ScheduleStopAfterMinutes(30);

    [RelayCommand]
    private void ScheduleStopAfter45Minutes() => ScheduleStopAfterMinutes(45);

    [RelayCommand]
    private void ScheduleStopAfter60Minutes() => ScheduleStopAfterMinutes(60);

    [RelayCommand]
    private void ScheduleStopAfter90Minutes() => ScheduleStopAfterMinutes(90);

    [RelayCommand]
    private void ScheduleCustomStopTimer()
    {
        if (!CanScheduleStopTimer)
        {
            return;
        }

        if (!int.TryParse(
                CustomStopMinutesText,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var minutes) ||
            minutes is < 1 or > 1440)
        {
            CustomStopTimerErrorText = "请输入 1 到 1440 分钟。";
            return;
        }

        CustomStopTimerErrorText = string.Empty;
        ScheduleStopAfterMinutes(minutes);
    }

    [RelayCommand]
    private void CancelStopTimer()
    {
        _stopTimer.Cancel();
        IsStopTimerMenuOpen = false;
    }

    [RelayCommand]
    private async Task OpenRulesManagementAsync(CancellationToken cancellationToken)
    {
        CloseTransientPanels();
        await _navigator.NavigateAsync(AppRoutes.TtsRules, cancellationToken).ConfigureAwait(true);
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private Task OpenMiniPlayerAsync(CancellationToken cancellationToken) =>
        _miniPlayerLauncher.OpenMiniPlayerAsync(cancellationToken);

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task TogglePlayPauseAsync(CancellationToken cancellationToken)
    {
        if (!HasAvailableRule || CurrentPlaybackState == PlaybackState.Faulted)
        {
            return;
        }

        if (CurrentPlaybackState == PlaybackState.Playing)
        {
            await _playbackCoordinator.PauseAsync(cancellationToken);
            return;
        }

        if (CurrentPlaybackState == PlaybackState.Paused)
        {
            await _playbackCoordinator.ResumeAsync(cancellationToken);
            return;
        }

        var snapshot = _playbackCoordinator.CurrentSnapshot;
        var bookId = snapshot.BookId ?? _requestedBookId ?? _contentProjection.LoadedBook?.BookId;
        if (string.IsNullOrWhiteSpace(bookId))
        {
            return;
        }

        await _playbackCoordinator.StartAsync(
            new PlaybackStartRequest(
                bookId,
                snapshot.ChapterIndex >= 0 ? snapshot.ChapterIndex : CurrentChapterIndex,
                snapshot.SegmentIndex >= 0 ? snapshot.SegmentIndex : CurrentSegmentIndex,
                null,
                SpeakSpeed),
            cancellationToken);
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task PreviousSegmentAsync(CancellationToken cancellationToken)
    {
        ResumeAutoCenterForExplicitNavigation();
        await _playbackCoordinator.PreviousSegmentAsync(cancellationToken);
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task NextSegmentAsync(CancellationToken cancellationToken)
    {
        ResumeAutoCenterForExplicitNavigation();
        await _playbackCoordinator.NextSegmentAsync(cancellationToken);
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task PreviousChapterAsync(CancellationToken cancellationToken)
    {
        ResumeAutoCenterForExplicitNavigation();
        await _playbackCoordinator.PreviousChapterAsync(cancellationToken);
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task NextChapterAsync(CancellationToken cancellationToken)
    {
        ResumeAutoCenterForExplicitNavigation();
        await _playbackCoordinator.NextChapterAsync(cancellationToken);
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task SelectRuleAsync(PlayerRuleItemViewModel? rule, CancellationToken cancellationToken)
    {
        if (rule is null || !rule.IsEnabled || rule.IsSelected || _playbackCoordinator.CurrentSnapshot.RuleId == rule.Id)
        {
            return;
        }

        await _rulesAndSpeedController.ChangeRuleAsync(rule.Id, cancellationToken);
        await RefreshRulesAsync(cancellationToken);
        IsRuleMenuOpen = false;
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private Task ApplySpeakSpeedAsync(CancellationToken cancellationToken)
    {
        return CommitSpeakSpeedAsync(cancellationToken);
    }

    internal async Task CommitSpeakSpeedAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _rulesAndSpeedController.CancelPendingSpeakSpeedChange();
        if (!_rulesAndSpeedController.TryParseSpeakSpeed(
                SpeedEditorText,
                out var parsedSpeed,
                out var errorText))
        {
            SpeedEditorErrorText = errorText;
            return;
        }

        SpeedEditorErrorText = string.Empty;
        await ApplySpeakSpeedChangeAsync(parsedSpeed, cancellationToken);
    }

    [RelayCommand]
    private void IncreaseSpeakSpeed()
    {
        var currentSpeed = _rulesAndSpeedController.ResolvePendingSpeakSpeed(SpeedEditorText, SpeakSpeed);
        var nextSpeed = Math.Min(currentSpeed + 1, AppSettings.MaxSpeakSpeed);
        if (nextSpeed == currentSpeed)
        {
            return;
        }

        SpeedEditorText = nextSpeed.ToString(CultureInfo.InvariantCulture);
        SpeedEditorErrorText = string.Empty;
        SpeakSpeed = nextSpeed;
        _rulesAndSpeedController.ScheduleSpeakSpeedChange(nextSpeed);
    }

    [RelayCommand]
    private void DecreaseSpeakSpeed()
    {
        var currentSpeed = _rulesAndSpeedController.ResolvePendingSpeakSpeed(SpeedEditorText, SpeakSpeed);
        var nextSpeed = Math.Max(currentSpeed - 1, AppSettings.MinSpeakSpeed);
        if (nextSpeed == currentSpeed)
        {
            return;
        }

        SpeedEditorText = nextSpeed.ToString(CultureInfo.InvariantCulture);
        SpeedEditorErrorText = string.Empty;
        SpeakSpeed = nextSpeed;
        _rulesAndSpeedController.ScheduleSpeakSpeedChange(nextSpeed);
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task SelectChapterAsync(PlayerChapterItemViewModel? chapter, CancellationToken cancellationToken)
    {
        await HandleChapterClickAsync(chapter, DesktopSelectionModifiers.None, cancellationToken);
    }

    public async Task HandleChapterClickAsync(
        PlayerChapterItemViewModel? chapter,
        DesktopSelectionModifiers modifiers,
        CancellationToken cancellationToken)
    {
        if (chapter is null)
        {
            return;
        }

        if (_activeCacheSelection.HandleChapterClick(chapter.ChapterIndex, modifiers))
        {
            return;
        }

        if (chapter.ChapterIndex == CurrentChapterIndex)
        {
            ResumeAutoCenterAndRequest(animate: true);
            return;
        }

        ResumeAutoCenterForExplicitNavigation();
        await _playbackCoordinator.JumpToChapterAsync(chapter.ChapterIndex, cancellationToken);
    }

    [RelayCommand]
    private void EnterActiveCacheSelection()
    {
        CloseTransientPanels();
        _activeCacheSelection.ApplySnapshot(_activeCacheCoordinator.CurrentSnapshot);
        _activeCacheSelection.EnterSelectionMode();
    }

    [RelayCommand]
    private void CancelActiveCacheSelection()
    {
        _activeCacheSelection.ExitSelectionMode();
    }

    [RelayCommand]
    private void SelectAllActiveCacheChapters()
    {
        _activeCacheSelection.SelectAll();
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task StartActiveCacheAsync(CancellationToken cancellationToken)
    {
        var bookId = _contentProjection.LoadedBook?.BookId ??
                     _playbackCoordinator.CurrentSnapshot.BookId ??
                     _requestedBookId;
        if (string.IsNullOrWhiteSpace(bookId))
        {
            return;
        }

        await _activeCacheSelection.StartAsync(bookId, SpeakSpeed, cancellationToken);
    }

    public bool HandleActiveCacheEscape() => _activeCacheSelection.ExitSelectionMode();

    public bool HandleActiveCacheSelectAll()
    {
        if (!IsActiveCacheSelectionMode)
        {
            return false;
        }

        _activeCacheSelection.SelectAll();
        return true;
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task SelectSegmentAsync(PlayerSegmentItemViewModel? segment, CancellationToken cancellationToken)
    {
        if (segment is null)
        {
            return;
        }

        if (segment.ChapterIndex == CurrentChapterIndex && segment.SegmentIndex == CurrentSegmentIndex)
        {
            ResumeAutoCenterAndRequest(animate: true);
            return;
        }

        ResumeAutoCenterForExplicitNavigation();
        await _playbackCoordinator.JumpToSegmentAsync(segment.ChapterIndex, segment.SegmentIndex, cancellationToken);
    }

    [RelayCommand]
    private void ReturnToCurrentSegment()
    {
        ResumeAutoCenterAndRequest(animate: false);
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private Task RetryCurrentSegmentAsync(CancellationToken cancellationToken)
    {
        return _playbackCoordinator.RetryCurrentSegmentAsync(cancellationToken);
    }

    partial void OnSpeakSpeedChanged(int value)
    {
        OnPropertyChanged(nameof(SpeakSpeedButtonText));
        OnPropertyChanged(nameof(CanDecreaseSpeakSpeed));
        OnPropertyChanged(nameof(CanIncreaseSpeakSpeed));
    }

    partial void OnCurrentSegmentIndexChanged(int value)
    {
        OnPropertyChanged(nameof(DisplayedSegmentCounterText));
        if (!IsSegmentProgressDragging)
        {
            SegmentProgressValue = NormalizeSegmentProgressValue(value);
            SegmentProgressPreviewValue = SegmentProgressValue;
        }
    }

    partial void OnHasActiveStopTimerChanged(bool value)
    {
        OnPropertyChanged(nameof(StopTimerButtonAutomationName));
    }

    partial void OnStopTimerRemainingTextChanged(string value)
    {
        OnPropertyChanged(nameof(StopTimerButtonAutomationName));
    }

    partial void OnCurrentChapterSegmentCountChanged(int value)
    {
        OnPropertyChanged(nameof(DisplayedSegmentCounterText));
        OnPropertyChanged(nameof(SegmentProgressMaximum));
        SegmentProgressValue = NormalizeSegmentProgressValue(CurrentSegmentIndex);
        if (!IsSegmentProgressDragging)
        {
            SegmentProgressPreviewValue = SegmentProgressValue;
        }
    }

    partial void OnIsSegmentProgressDraggingChanged(bool value)
    {
        OnPropertyChanged(nameof(DisplayedSegmentCounterText));
    }

    partial void OnIsFaultedChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowPlaybackErrorBar));
        OnPropertyChanged(nameof(CanTogglePlayPause));
    }

    partial void OnCurrentPlaybackStateChanged(PlaybackState value)
    {
        OnPropertyChanged(nameof(ShowInlineLoadingState));
        OnPropertyChanged(nameof(InlineLoadingText));
        OnPropertyChanged(nameof(PrimaryAction));
    }

    partial void OnHasAvailableRuleChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowPlaybackControls));
        OnPropertyChanged(nameof(ShowNoRuleState));
        OnPropertyChanged(nameof(CanTogglePlayPause));
    }

    partial void OnErrorTextChanged(string value)
    {
        OnPropertyChanged(nameof(ShowPlaybackErrorBar));
    }

    private void OnSnapshotChanged(object? sender, PlaybackSnapshot snapshot)
    {
        if (!_uiScheduler.CheckAccess())
        {
            _pageTasks.Register(
                _uiScheduler.InvokeAsync(() => HandleSnapshotUpdateAsync(snapshot)),
                exception => ReportViewOperationFailure("更新播放页面失败", exception));
            return;
        }

        _pageTasks.Register(
            HandleSnapshotUpdateAsync(snapshot),
            exception => ReportViewOperationFailure("更新播放页面失败", exception));
    }

    private void OnStopTimerSnapshotChanged(object? sender, PlaybackStopTimerSnapshot snapshot)
    {
        if (!_uiScheduler.CheckAccess())
        {
            _pageTasks.Register(
                _uiScheduler.InvokeAsync(() => ApplyStopTimerSnapshot(snapshot), _pageEventCancellation.Token),
                exception => ReportViewOperationFailure("更新定时停止状态失败", exception));
            return;
        }

        ApplyStopTimerSnapshot(snapshot);
    }

    private void OnActiveCacheSnapshotChanged(object? sender, ActiveCacheSnapshot snapshot)
    {
        if (!_uiScheduler.CheckAccess())
        {
            _pageTasks.Register(
                _uiScheduler.InvokeAsync(() => _activeCacheSelection.ApplySnapshot(snapshot)),
                exception => ReportViewOperationFailure("更新主动缓存状态失败", exception));
            return;
        }

        _activeCacheSelection.ApplySnapshot(snapshot);
    }

    private void OnCacheChanged(object? sender, CacheChangedEventArgs eventArgs)
    {
        var loadedBookId = _contentProjection.LoadedBook?.BookId;
        if (string.IsNullOrWhiteSpace(loadedBookId) ||
            (!string.IsNullOrWhiteSpace(eventArgs.BookId) &&
             !string.Equals(eventArgs.BookId, loadedBookId, StringComparison.Ordinal)))
        {
            return;
        }

        ScheduleCacheStatusRefresh(eventArgs.ChapterIndex);
    }

    private void OnSettingsChanged(object? sender, AppSettingsChangedEventArgs eventArgs)
    {
        if (eventArgs.Previous.DefaultSpeakSpeed == eventArgs.Current.DefaultSpeakSpeed &&
            eventArgs.Previous.SelectedTtsRuleId == eventArgs.Current.SelectedTtsRuleId &&
            eventArgs.Previous.EnableLongParagraphSplitting == eventArgs.Current.EnableLongParagraphSplitting &&
            eventArgs.Previous.LongParagraphThreshold == eventArgs.Current.LongParagraphThreshold &&
            eventArgs.Previous.ReadChapterTitle == eventArgs.Current.ReadChapterTitle)
        {
            return;
        }

        ScheduleCacheStatusRefresh(chapterIndex: null);
    }

    private void OnActiveCacheSelectionStateChanged(object? sender, EventArgs e)
    {
        foreach (var chapter in Chapters)
        {
            chapter.IsSelectedForActiveCache = _activeCacheSelection.IsSelected(chapter.ChapterIndex);
        }

        OnPropertyChanged(nameof(IsActiveCacheSelectionMode));
        OnPropertyChanged(nameof(SelectedActiveCacheChapterCount));
        OnPropertyChanged(nameof(ActiveCacheSelectionSummary));
        OnPropertyChanged(nameof(ActiveCacheStatusText));
        OnPropertyChanged(nameof(HasActiveCacheBatch));
        OnPropertyChanged(nameof(CanStartActiveCache));
    }

    private void OnAutoScrollStateChanged(object? sender, EventArgs e)
    {
        if (!_uiScheduler.CheckAccess())
        {
            _pageTasks.Register(
                _uiScheduler.InvokeAsync(ApplyAutoScrollState),
                exception => ReportViewOperationFailure("更新滚动状态失败", exception));
            return;
        }

        ApplyAutoScrollState();
    }

    private async Task HandleSnapshotUpdateAsync(PlaybackSnapshot snapshot)
    {
        var previousSnapshot = _lastAppliedSnapshot;
        ApplySnapshot(snapshot);

        if (string.IsNullOrWhiteSpace(snapshot.BookId))
        {
            return;
        }

        try
        {
            await EnsureContentLoadedForSnapshotAsync(snapshot, _pageEventCancellation.Token);
            if (previousSnapshot.RuleId != snapshot.RuleId ||
                previousSnapshot.SpeakSpeed != snapshot.SpeakSpeed ||
                previousSnapshot.ContentRevision != snapshot.ContentRevision)
            {
                QueueCacheStatusRefresh(chapterIndex: null);
            }

            if (_autoScrollCoordinator.ShouldAutoCenter &&
                ShouldAnimateCenteringForSnapshotUpdate(previousSnapshot, snapshot))
            {
                RequestCurrentSegmentCentering(animate: true);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task RefreshRulesAsync(CancellationToken cancellationToken)
    {
        var rules = await _rulesAndSpeedController.LoadRulesAsync(cancellationToken);
        Rules.ReplaceWith(rules, static rule => rule);
        ApplyRuleSelection(_playbackCoordinator.CurrentSnapshot.RuleId);
        OnPropertyChanged(nameof(HasRules));
    }

    private async Task EnsureContentLoadedForSnapshotAsync(PlaybackSnapshot snapshot, CancellationToken cancellationToken)
    {
        await _contentProjection.EnsureContentLoadedAsync(snapshot, cancellationToken);
        SynchronizeContentProjection(includeChapterTitle: true);
        if (_contentProjection.LoadedBook is { BookId: { Length: > 0 } bookId } &&
            !string.Equals(_cacheStatusInitializedBookId, bookId, StringComparison.Ordinal))
        {
            _cacheStatusInitializedBookId = bookId;
            QueueCacheStatusRefresh(chapterIndex: null);
        }
    }

    private void ScheduleCacheStatusRefresh(int? chapterIndex)
    {
        if (!_uiScheduler.CheckAccess())
        {
            _pageTasks.Register(
                _uiScheduler.InvokeAsync(() => QueueCacheStatusRefresh(chapterIndex), _pageEventCancellation.Token),
                exception => ReportViewOperationFailure("刷新章节缓存进度失败", exception));
            return;
        }

        QueueCacheStatusRefresh(chapterIndex);
    }

    private void QueueCacheStatusRefresh(int? chapterIndex)
    {
        if (!_isPageEventsRegistered ||
            _pageEventCancellation.IsCancellationRequested ||
            _contentProjection.LoadedBook is not { BookId: { Length: > 0 } bookId })
        {
            return;
        }

        var chapterIndices = chapterIndex is null
            ? Chapters.Select(static chapter => chapter.ChapterIndex)
            : Chapters.Where(chapter => chapter.ChapterIndex == chapterIndex.Value)
                .Select(static chapter => chapter.ChapterIndex);

        _cacheStatusRefresh.Request(bookId, chapterIndices.ToArray());
    }

    private void ApplyChapterCacheStatuses(
        string bookId,
        IReadOnlyCollection<int> requestedChapterIndices,
        IReadOnlyCollection<ChapterCacheStatus> statuses)
    {
        if (!_isPageEventsRegistered ||
            !string.Equals(_contentProjection.LoadedBook?.BookId, bookId, StringComparison.Ordinal))
        {
            return;
        }

        var statusesByChapter = statuses.ToDictionary(static status => status.ChapterIndex);
        foreach (var chapter in Chapters.Where(chapter => requestedChapterIndices.Contains(chapter.ChapterIndex)))
        {
            if (statusesByChapter.TryGetValue(chapter.ChapterIndex, out var status))
            {
                chapter.ApplyCacheStatus(status.CachedSegmentCount, status.TotalSegmentCount);
            }
            else
            {
                chapter.ApplyCacheStatus(0, totalSegmentCount: null);
            }
        }
    }

    private async Task<PlaybackBookContent?> EnsureBookLoadedAsync(string bookId, CancellationToken cancellationToken)
    {
        var book = await _contentProjection.EnsureBookLoadedAsync(
            bookId,
            CurrentChapterIndex,
            CurrentSegmentIndex,
            cancellationToken);
        SynchronizeContentProjection(includeChapterTitle: false);
        if (book is not null)
        {
            if (string.IsNullOrWhiteSpace(_playbackCoordinator.CurrentSnapshot.BookTitle))
            {
                CurrentTitle = book.BookTitle;
            }

            if (string.IsNullOrWhiteSpace(_playbackCoordinator.CurrentSnapshot.BookAuthor))
            {
                CurrentAuthor = string.IsNullOrWhiteSpace(book.BookAuthor) ? "未知作者" : book.BookAuthor;
            }
        }

        return book;
    }

    private void ApplySnapshot(PlaybackSnapshot snapshot)
    {
        _contentProjection.ApplyPosition(snapshot.ChapterIndex, snapshot.SegmentIndex, snapshot.SegmentCount);
        var projected = _snapshotProjection.Project(
            snapshot,
            _contentProjection.LoadedBook,
            _contentProjection.ResolveChapterTitle(snapshot.ChapterIndex),
            _rulesAndSpeedController.DefaultSpeakSpeed);

        CurrentPlaybackState = projected.PlaybackState;
        CurrentTitle = projected.Title;
        CurrentAuthor = projected.Author;
        CurrentChapterTitle = projected.ChapterTitle;
        IsFaulted = projected.IsFaulted;
        HasAvailableRule = projected.HasAvailableRule;
        ErrorText = projected.ErrorText;
        PrimaryActionText = projected.PrimaryActionText;
        if (projected.SpeakSpeed > 0)
        {
            SpeakSpeed = projected.SpeakSpeed;
            if (!IsSpeedMenuOpen)
            {
                SpeedEditorText = projected.SpeakSpeed.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        CurrentChapterIndex = projected.ChapterIndex;
        CurrentSegmentIndex = projected.SegmentIndex;
        SynchronizeContentProjection(includeChapterTitle: false);
        ApplyRuleSelection(snapshot.RuleId);
        _lastAppliedSnapshot = snapshot;
        OnPropertyChanged(nameof(CanScheduleStopTimer));
    }

    private void ApplyRuleSelection(long? selectedRuleId)
    {
        if (selectedRuleId is null)
        {
            return;
        }

        foreach (var rule in Rules)
        {
            rule.IsSelected = rule.Id == selectedRuleId.Value;
        }
    }

    private void SynchronizeContentProjection(bool includeChapterTitle)
    {
        _activeCacheSelection.SetChapters(Chapters.Select(chapter => chapter.ChapterIndex));
        CurrentChapterItem = _contentProjection.CurrentChapterItem;
        CurrentSegmentItem = _contentProjection.CurrentSegmentItem;
        CurrentChapterSegmentCount = _contentProjection.CurrentChapterSegmentCount;
        if (includeChapterTitle && !string.IsNullOrWhiteSpace(_contentProjection.CurrentChapterTitle))
        {
            CurrentChapterTitle = _contentProjection.CurrentChapterTitle;
        }

        CanGoToPreviousChapter = _contentProjection.CanGoToPreviousChapter;
        CanGoToNextChapter = _contentProjection.CanGoToNextChapter;
        CanGoToPreviousSegment = _contentProjection.CanGoToPreviousSegment;
        CanGoToNextSegment = _contentProjection.CanGoToNextSegment;
    }

    private void ApplyAutoScrollState()
    {
        var currentState = _autoScrollCoordinator.State;
        ShowReturnToCurrentSegment = _autoScrollCoordinator.ShowReturnToCurrentSegment;
        OnPropertyChanged(nameof(AutoScrollState));
        OnPropertyChanged(nameof(ShouldAutoCenterCurrentSegment));

        if (currentState == PlayerAutoScrollState.AutoCentering &&
            _lastAppliedAutoScrollState != PlayerAutoScrollState.AutoCentering)
        {
            if (_suppressNextStateDrivenAutoCenterRequest)
            {
                _suppressNextStateDrivenAutoCenterRequest = false;
            }
            else
            {
                RequestCurrentSegmentCentering(animate: false);
            }
        }
        else if (currentState != PlayerAutoScrollState.AutoCentering)
        {
            _suppressNextStateDrivenAutoCenterRequest = false;
        }

        _lastAppliedAutoScrollState = currentState;
    }

    private void ResumeAutoCenterAndRequest(bool animate)
    {
        ResumeAutoCenterForExplicitNavigation();
        RequestCurrentSegmentCentering(animate);
    }

    private void ResumeAutoCenterForExplicitNavigation()
    {
        if (_autoScrollCoordinator.State == PlayerAutoScrollState.AutoCentering)
        {
            return;
        }

        _suppressNextStateDrivenAutoCenterRequest = true;
        _autoScrollCoordinator.ResumeAutoCenter();
    }

    private void RequestCurrentSegmentCentering(bool animate)
    {
        _animateNextSegmentCenterRequest = animate;
        _segmentCenterRequestVersion++;
        OnPropertyChanged(nameof(AnimateNextSegmentCenterRequest));
        OnPropertyChanged(nameof(SegmentCenterRequestVersion));
    }

    private static bool ShouldAnimateCenteringForSnapshotUpdate(PlaybackSnapshot previousSnapshot, PlaybackSnapshot snapshot)
    {
        if (!string.Equals(previousSnapshot.BookId, snapshot.BookId, StringComparison.Ordinal))
        {
            return !string.IsNullOrWhiteSpace(snapshot.BookId);
        }

        return previousSnapshot.ChapterIndex != snapshot.ChapterIndex ||
               previousSnapshot.SegmentIndex != snapshot.SegmentIndex;
    }

    private void CloseTransientPanels()
    {
        IsRuleMenuOpen = false;
        IsSpeedMenuOpen = false;
        IsStopTimerMenuOpen = false;
        SpeedEditorErrorText = string.Empty;
    }

    private void ScheduleStopAfterMinutes(int minutes)
    {
        if (!CanScheduleStopTimer)
        {
            return;
        }

        _stopTimer.ScheduleAfter(TimeSpan.FromMinutes(minutes));
        IsStopTimerMenuOpen = false;
    }

    private void ApplyStopTimerSnapshot(PlaybackStopTimerSnapshot snapshot)
    {
        if (snapshot.Version <= _lastAppliedStopTimerVersion)
        {
            return;
        }

        _lastAppliedStopTimerVersion = snapshot.Version;
        HasActiveStopTimer = snapshot.IsActive;
        StopTimerRemainingText = FormatStopTimerRemaining(snapshot);
        UpdateStopTimerDisplayTimer(snapshot.IsActive);
    }

    private void OnStopTimerDisplayTick(object? state)
    {
        if (!_isPageEventsRegistered)
        {
            return;
        }

        if (!_uiScheduler.CheckAccess())
        {
            _pageTasks.Register(
                _uiScheduler.InvokeAsync(RefreshStopTimerDisplay, _pageEventCancellation.Token),
                exception => ReportViewOperationFailure("更新定时停止倒计时失败", exception));
            return;
        }

        RefreshStopTimerDisplay();
    }

    private void RefreshStopTimerDisplay()
    {
        var snapshot = _stopTimer.CurrentSnapshot;
        StopTimerRemainingText = FormatStopTimerRemaining(snapshot);
    }

    private void UpdateStopTimerDisplayTimer(bool isActive)
    {
        if (!isActive || !_isPageEventsRegistered)
        {
            StopStopTimerDisplayTimer();
            return;
        }

        _stopTimerDisplayTimer ??= _timeProvider.CreateTimer(
            OnStopTimerDisplayTick,
            state: null,
            dueTime: TimeSpan.FromSeconds(1),
            period: TimeSpan.FromSeconds(1));
        _stopTimerDisplayTimer.Change(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    private void StopStopTimerDisplayTimer()
    {
        _stopTimerDisplayTimer?.Dispose();
        _stopTimerDisplayTimer = null;
    }

    private DateTimeOffset GetCurrentUtcNow() => _timeProvider.GetUtcNow();

    private string FormatStopTimerRemaining(PlaybackStopTimerSnapshot snapshot)
    {
        if (!snapshot.IsActive || snapshot.DueAt is not { } dueAt)
        {
            return "—";
        }

        var remaining = dueAt - GetCurrentUtcNow();
        var minutes = remaining <= TimeSpan.Zero
            ? 0
            : Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes));
        return minutes.ToString(CultureInfo.InvariantCulture);
    }

    private int ResolveSpeakSpeedForOpen()
    {
        return AppSettings.NormalizeSpeakSpeed(
            SpeakSpeed > 0 ? SpeakSpeed : _rulesAndSpeedController.DefaultSpeakSpeed);
    }

    private async Task ApplySpeakSpeedChangeAsync(int parsedSpeed, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _rulesAndSpeedController.ApplySpeakSpeedAsync(parsedSpeed, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        SpeakSpeed = parsedSpeed;
        SpeedEditorText = parsedSpeed.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private async Task RestoreMissingRuleSessionAsync(
        string requestedBookId,
        PlaybackSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        if (snapshot.HasAvailableRule ||
            string.IsNullOrWhiteSpace(snapshot.BookId) ||
            !string.Equals(snapshot.BookId, requestedBookId, StringComparison.Ordinal))
        {
            return;
        }

        var selectedRule = Rules.FirstOrDefault(static rule => rule.IsEnabled && rule.IsSelected);
        if (selectedRule is null)
        {
            return;
        }

        await _playbackCoordinator.OpenPausedAsync(
            new OpenBookPlaybackRequest(
                requestedBookId,
                snapshot.ChapterIndex >= 0 ? snapshot.ChapterIndex : null,
                snapshot.SegmentIndex >= 0 ? snapshot.SegmentIndex : null,
                ResolveSpeakSpeedForOpen()),
            cancellationToken);

        var refreshedSnapshot = _playbackCoordinator.CurrentSnapshot;
        ApplySnapshot(refreshedSnapshot);
        await EnsureContentLoadedForSnapshotAsync(refreshedSnapshot, cancellationToken);
    }

    private async Task HandleMissingBookAsync(CancellationToken cancellationToken)
    {
        _feedbackService.ShowWarning("无法打开书籍", "这本书可能已经被删除。");
        await _navigator.NavigateAsync(AppRoutes.Library, cancellationToken).ConfigureAwait(true);
    }

    private double NormalizeSegmentProgressValue(double value)
    {
        if (CurrentChapterSegmentCount <= 0)
        {
            return 0d;
        }

        return Math.Clamp(Math.Round(value), 0d, SegmentProgressMaximum);
    }

    private static string BuildSegmentCounterText(int segmentIndex, int segmentCount)
    {
        return segmentCount > 0 && segmentIndex >= 0
            ? $"{segmentIndex + 1} / {segmentCount}"
            : "尚未定位段落";
    }
}
