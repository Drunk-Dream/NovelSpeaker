using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovelSpeaker.Application.Books;
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

public sealed partial class PlayerViewModel : ObservableObject, ISegmentProgressInteractionTarget, ITransientEscapeHandler
{
    private readonly IPlaybackSession _playbackCoordinator;
    private readonly IPlaybackStopTimer _stopTimer;
    private readonly IAppNavigator _navigator;
    private readonly IUiScheduler _uiScheduler;
    private readonly ResettableObservableCollection<PlayerRuleItemViewModel> _rules = [];
    private readonly IAppFeedbackService _feedbackService;
    private readonly IMiniPlayerLauncher _miniPlayerLauncher;
    private readonly TimeProvider _timeProvider;
    private readonly PlayerContentController _contentController;
    private readonly PlayerPlaybackProjection _playbackProjection;
    private readonly PlayerSpeechControlController _speechControlController;
    private readonly PlayerCacheDecorationController _cacheDecorationController;
    private readonly PlayerInteractionController _interactionController;
    private readonly OwnedTaskRegistry _pageTasks = new();

    private string? _requestedBookId;
    private PlaybackSnapshot _lastAppliedSnapshot = PlaybackSnapshot.Idle;
    private long _lastAppliedStopTimerVersion = -1;
    private ITimer? _stopTimerDisplayTimer;
    private CancellationTokenSource _pageEventCancellation = new();
    private bool _isPageEventsRegistered;
    private int _pageEventGeneration;

    public PlayerViewModel(
        IPlaybackSession playbackCoordinator,
        IPlaybackStopTimer stopTimer,
        IActiveCacheCoordinator activeCacheCoordinator,
        IBookDetailsQuery bookDetailsQuery,
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
        _feedbackService = feedbackService;
        _miniPlayerLauncher = miniPlayerLauncher ?? throw new ArgumentNullException(nameof(miniPlayerLauncher));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _navigator = navigator;
        _uiScheduler = uiScheduler ?? new WpfUiScheduler();
        _contentController = new PlayerContentController(
            bookDetailsQuery,
            bookPlaybackContentService,
            _uiScheduler);
        _playbackProjection = new PlayerPlaybackProjection();
        _speechControlController = new PlayerSpeechControlController(
            playbackCoordinator,
            ruleQueries,
            settingsService,
            feedbackService,
            _timeProvider);
        _cacheDecorationController = new PlayerCacheDecorationController(
            activeCacheCoordinator,
            cacheWorkspaceService,
            settingsService,
            _contentController,
            _uiScheduler,
            ReportViewOperationFailure);
        _cacheDecorationController.StateChanged += OnCacheDecorationStateChanged;
        _interactionController = new PlayerInteractionController(playbackCoordinator, autoScrollCoordinator);
        _interactionController.StateChanged += OnInteractionStateChanged;

        ApplySnapshot(_playbackCoordinator.CurrentSnapshot);
        ApplyStopTimerSnapshot(_stopTimer.CurrentSnapshot);
    }

    public ObservableCollection<PlayerRuleItemViewModel> Rules => _rules;

    public ObservableCollection<PlayerChapterItemViewModel> Chapters => _contentController.Chapters;

    public ObservableCollection<PlayerSegmentItemViewModel> Segments => _contentController.Segments;

    public int? CurrentChapterPosition => _contentController.GetChapterPosition(CurrentChapterIndex);

    public bool HasRules => Rules.Count > 0;

    public bool ShowPlaybackControls => HasAvailableRule;

    public bool ShowNoRuleState => !HasAvailableRule;

    public bool ShowPlaybackErrorBar => IsFaulted && !string.IsNullOrWhiteSpace(ErrorText);

    public bool ShowEmptyChapterState =>
        IsCurrentChapterContentLoaded && CurrentChapterSegmentCount == 0 && HasAvailableRule;

    public bool CanTogglePlayPause => HasAvailableRule && !IsFaulted && !ShowEmptyChapterState;

    public bool CanDecreaseSpeakSpeed => SpeakSpeed > AppSettings.MinSpeakSpeed;

    public bool CanIncreaseSpeakSpeed => SpeakSpeed < AppSettings.MaxSpeakSpeed;

    public bool IsActiveCacheSelectionMode => _cacheDecorationController.IsSelectionMode;

    public int SelectedActiveCacheChapterCount => _cacheDecorationController.SelectedChapterCount;

    public string ActiveCacheSelectionSummary => _cacheDecorationController.SelectionSummary;

    public string ActiveCacheStatusText => _cacheDecorationController.StatusText;

    public bool HasActiveCacheBatch => _cacheDecorationController.HasActiveBatch;

    public bool CanStartActiveCache => _cacheDecorationController.CanStart;

    public bool CanScheduleStopTimer =>
        !string.IsNullOrWhiteSpace(_playbackCoordinator.CurrentSnapshot.BookId) &&
        CurrentPlaybackState is PlaybackState.Playing or PlaybackState.Paused;

    public string SpeakSpeedButtonText => $"语速 {SpeakSpeed}";

    public string VolumePercentText => $"{Math.Round(Volume * 100d):0}%";

    public string VolumeButtonAutomationName => $"播放音量 {VolumePercentText}";

    public string StopTimerButtonAutomationName => HasActiveStopTimer
        ? $"定时停止，剩余 {StopTimerRemainingText}"
        : "定时停止";

    public string StopTimerPresetMinutesText { get; private set; } = string.Empty;

    public PlaybackPrimaryAction PrimaryAction => CurrentPlaybackState == PlaybackState.Playing
        ? PlaybackPrimaryAction.Pause
        : PlaybackPrimaryAction.Play;

    public string DisplayedSegmentCounterText => ShowEmptyChapterState
        ? "0 / 0"
        : BuildSegmentCounterText(
            IsSegmentProgressDragging ? (int)Math.Round(SegmentProgressPreviewValue) : CurrentSegmentIndex,
            CurrentChapterSegmentCount);

    public PlayerAutoScrollState AutoScrollState => _interactionController.AutoScrollState;

    public bool ShouldAutoCenterCurrentSegment => _interactionController.ShouldAutoCenterCurrentSegment;

    public bool ShowInlineLoadingState => CurrentPlaybackState is PlaybackState.Preparing or PlaybackState.Buffering or PlaybackState.Recovering;

    public string InlineLoadingText => CurrentPlaybackState switch
    {
        PlaybackState.Preparing => "正在准备",
        PlaybackState.Buffering => "正在加载",
        PlaybackState.Recovering => "正在恢复",
        _ => string.Empty
    };

    public double SegmentProgressMaximum => Math.Max(CurrentChapterSegmentCount - 1, 0);

    public int SegmentCenterRequestVersion => _interactionController.SegmentCenterRequestVersion;

    public bool AnimateNextSegmentCenterRequest => _interactionController.AnimateNextSegmentCenterRequest;

    [ObservableProperty]
    private string currentTitle = "未打开书籍";

    [ObservableProperty]
    private string currentAuthor = "未知作者";

    [ObservableProperty]
    private string currentChapterTitle = "尚未定位章节";

    [ObservableProperty]
    private string errorText = string.Empty;

    [ObservableProperty]
    private bool isCurrentChapterContentLoaded;

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
    private bool isVolumeMenuOpen;

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

    public bool ShowReturnToCurrentSegment => _interactionController.ShowReturnToCurrentSegment;

    [ObservableProperty]
    private double segmentProgressValue;

    [ObservableProperty]
    private double segmentProgressPreviewValue;

    [ObservableProperty]
    private bool isSegmentProgressDragging;

    [ObservableProperty]
    private double volume = PlaybackVolume.Default;

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _speechControlController.RefreshDefaultSpeakSpeed();

        if (string.IsNullOrWhiteSpace(_playbackCoordinator.CurrentSnapshot.BookId) ||
            !AppSettings.IsValidSpeakSpeed(_playbackCoordinator.CurrentSnapshot.SpeakSpeed))
        {
            SpeakSpeed = _speechControlController.DefaultSpeakSpeed;
        }

        await RefreshRulesAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        ApplySnapshot(_playbackCoordinator.CurrentSnapshot);
    }

    public async Task HandleNavigationAsync(PlayerRoute request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        _requestedBookId = request.BookId;
        _cacheDecorationController.ExitSelectionMode();
        CloseTransientPanels();
        _interactionController.ResumeAutoCenterForExplicitNavigation();

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
            var previousChapterIndex = snapshot.ChapterIndex;
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
            if (previousChapterIndex != snapshot.ChapterIndex)
            {
                _cacheDecorationController.RequestStatusRefresh(chapterIndex: null);
            }
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
        Interlocked.Increment(ref _pageEventGeneration);
        _pageEventCancellation.Cancel();
        _contentController.InvalidatePendingLoads();
        StopStopTimerDisplayTimer();
        _cacheDecorationController.Deactivate();
        _speechControlController.CancelPendingSpeakSpeedChange();
        CloseTransientPanels();
        _interactionController.Deactivate();
        if (!_isPageEventsRegistered)
        {
            return;
        }

        _playbackCoordinator.SnapshotChanged -= OnSnapshotChanged;
        _stopTimer.SnapshotChanged -= OnStopTimerSnapshotChanged;
        _isPageEventsRegistered = false;
    }

    public void OnPageNavigatedTo(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _pageEventGeneration);
        _pageEventCancellation.Dispose();
        _pageEventCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _cacheDecorationController.Activate(_pageEventCancellation.Token);
        _interactionController.Activate();
        RegisterPageEvents();
        ApplySnapshot(_playbackCoordinator.CurrentSnapshot);
        _cacheDecorationController.RequestStatusRefresh(chapterIndex: null);
        var stopTimerSnapshot = _stopTimer.CurrentSnapshot;
        ApplyStopTimerSnapshot(stopTimerSnapshot);
        RefreshStopTimerDisplay();
        UpdateStopTimerDisplayTimer(stopTimerSnapshot.IsActive);
    }

    private void RegisterPageEvents()
    {
        if (_isPageEventsRegistered)
        {
            return;
        }

        _playbackCoordinator.SnapshotChanged += OnSnapshotChanged;
        _stopTimer.SnapshotChanged += OnStopTimerSnapshotChanged;
        _isPageEventsRegistered = true;
    }

    public void NotifyUserScrollInput()
    {
        _interactionController.NotifyUserScrollInput();
    }

    public void NotifyPassiveSegmentScrollChange()
    {
        _interactionController.NotifyPassiveSegmentScrollChange();
    }

    public void NotifyScrollbarDragStarted()
    {
        _interactionController.NotifyScrollbarDragStarted();
    }

    public void NotifyScrollbarDragCompleted()
    {
        _interactionController.NotifyScrollbarDragCompleted();
    }

    public void NotifyProgrammaticScrollStarted()
    {
        _interactionController.NotifyProgrammaticScrollStarted();
    }

    public void NotifyProgrammaticScrollCompleted()
    {
        _interactionController.NotifyProgrammaticScrollCompleted();
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
            _interactionController.ResumeAutoCenterAndRequest(animate: true);
            return;
        }

        await _interactionController.JumpToSegmentAsync(
            CurrentChapterIndex,
            targetSegmentIndex,
            cancellationToken);
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
        await _navigator.NavigateBackAsync(cancellationToken).ConfigureAwait(true);
    }

    [RelayCommand]
    private void ToggleRuleMenu()
    {
        IsSpeedMenuOpen = false;
        IsStopTimerMenuOpen = false;
        IsVolumeMenuOpen = false;
        IsRuleMenuOpen = !IsRuleMenuOpen;
    }

    [RelayCommand]
    private void ToggleSpeedMenu()
    {
        IsRuleMenuOpen = false;
        IsStopTimerMenuOpen = false;
        IsVolumeMenuOpen = false;
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
        IsVolumeMenuOpen = false;
        IsRuleMenuOpen = true;
    }

    [RelayCommand]
    private void ToggleStopTimerMenu()
    {
        IsRuleMenuOpen = false;
        IsSpeedMenuOpen = false;
        IsVolumeMenuOpen = false;
        CustomStopTimerErrorText = string.Empty;
        IsStopTimerMenuOpen = !IsStopTimerMenuOpen;
    }

    [RelayCommand]
    private void ToggleVolumeMenu()
    {
        IsRuleMenuOpen = false;
        IsSpeedMenuOpen = false;
        IsStopTimerMenuOpen = false;
        IsVolumeMenuOpen = !IsVolumeMenuOpen;
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
        if (!CanTogglePlayPause)
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
        var bookId = snapshot.BookId ?? _requestedBookId ?? _contentController.LoadedBook?.BookId;
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
        await _interactionController.PreviousSegmentAsync(cancellationToken);
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task NextSegmentAsync(CancellationToken cancellationToken)
    {
        await _interactionController.NextSegmentAsync(cancellationToken);
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task PreviousChapterAsync(CancellationToken cancellationToken)
    {
        await _interactionController.PreviousChapterAsync(cancellationToken);
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task NextChapterAsync(CancellationToken cancellationToken)
    {
        await _interactionController.NextChapterAsync(cancellationToken);
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task SelectRuleAsync(PlayerRuleItemViewModel? rule, CancellationToken cancellationToken)
    {
        if (rule is null || !rule.IsEnabled || rule.IsSelected || _playbackCoordinator.CurrentSnapshot.RuleId == rule.Id)
        {
            return;
        }

        await _speechControlController.ChangeRuleAsync(rule.Id, cancellationToken);
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
        _speechControlController.CancelPendingSpeakSpeedChange();
        if (!_speechControlController.TryParseSpeakSpeed(
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
        var currentSpeed = _speechControlController.ResolvePendingSpeakSpeed(SpeedEditorText, SpeakSpeed);
        var nextSpeed = Math.Min(currentSpeed + 1, AppSettings.MaxSpeakSpeed);
        if (nextSpeed == currentSpeed)
        {
            return;
        }

        SpeedEditorText = nextSpeed.ToString(CultureInfo.InvariantCulture);
        SpeedEditorErrorText = string.Empty;
        SpeakSpeed = nextSpeed;
        _speechControlController.ScheduleSpeakSpeedChange(nextSpeed);
    }

    [RelayCommand]
    private void DecreaseSpeakSpeed()
    {
        var currentSpeed = _speechControlController.ResolvePendingSpeakSpeed(SpeedEditorText, SpeakSpeed);
        var nextSpeed = Math.Max(currentSpeed - 1, AppSettings.MinSpeakSpeed);
        if (nextSpeed == currentSpeed)
        {
            return;
        }

        SpeedEditorText = nextSpeed.ToString(CultureInfo.InvariantCulture);
        SpeedEditorErrorText = string.Empty;
        SpeakSpeed = nextSpeed;
        _speechControlController.ScheduleSpeakSpeedChange(nextSpeed);
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

        if (_cacheDecorationController.HandleChapterClick(chapter.ChapterIndex, modifiers))
        {
            return;
        }

        if (chapter.ChapterIndex == CurrentChapterIndex)
        {
            _interactionController.ResumeAutoCenterAndRequest(animate: true);
            return;
        }

        await _interactionController.JumpToChapterAsync(chapter.ChapterIndex, cancellationToken);
    }

    internal void RequestCacheDecorationWindow(int start, int count) =>
        _cacheDecorationController.RequestDecorationWindow(start, count);

    [RelayCommand]
    private void EnterActiveCacheSelection()
    {
        CloseTransientPanels();
        _cacheDecorationController.EnterSelectionMode();
    }

    [RelayCommand]
    private void CancelActiveCacheSelection()
    {
        _cacheDecorationController.ExitSelectionMode();
    }

    [RelayCommand]
    private void SelectAllActiveCacheChapters()
    {
        _cacheDecorationController.SelectAll();
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task StartActiveCacheAsync(CancellationToken cancellationToken)
    {
        var bookId = _contentController.LoadedBook?.BookId ??
                     _playbackCoordinator.CurrentSnapshot.BookId ??
                     _requestedBookId;
        if (string.IsNullOrWhiteSpace(bookId))
        {
            return;
        }

        await _cacheDecorationController.StartAsync(bookId, SpeakSpeed, cancellationToken);
    }

    public bool HandleActiveCacheEscape() => _cacheDecorationController.TryExitSelectionMode();

    public bool TryHandleEscape()
    {
        if (IsSegmentProgressDragging)
        {
            CancelSegmentProgressInteraction();
            return true;
        }

        if (IsActiveCacheSelectionMode)
        {
            return HandleActiveCacheEscape();
        }

        if (IsRuleMenuOpen || IsSpeedMenuOpen || IsStopTimerMenuOpen || IsVolumeMenuOpen)
        {
            CloseTransientPanels();
            return true;
        }

        return false;
    }

    public bool HandleActiveCacheSelectAll()
    {
        if (!IsActiveCacheSelectionMode)
        {
            return false;
        }

        _cacheDecorationController.SelectAll();
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
            _interactionController.ResumeAutoCenterAndRequest(animate: true);
            return;
        }

        await _interactionController.JumpToSegmentAsync(
            segment.ChapterIndex,
            segment.SegmentIndex,
            cancellationToken);
    }

    [RelayCommand]
    private void ReturnToCurrentSegment()
    {
        _interactionController.ResumeAutoCenterAndRequest(animate: false);
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

    partial void OnVolumeChanged(double value)
    {
        var normalized = PlaybackVolume.Normalize(value);
        if (normalized != value)
        {
            Volume = normalized;
            return;
        }

        OnPropertyChanged(nameof(VolumePercentText));
        OnPropertyChanged(nameof(VolumeButtonAutomationName));
        _playbackCoordinator.SetVolume(normalized);
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
        OnPropertyChanged(nameof(ShowEmptyChapterState));
        OnPropertyChanged(nameof(CanTogglePlayPause));
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
        OnPropertyChanged(nameof(ShowEmptyChapterState));
    }

    partial void OnIsCurrentChapterContentLoadedChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowEmptyChapterState));
        OnPropertyChanged(nameof(CanTogglePlayPause));
        OnPropertyChanged(nameof(DisplayedSegmentCounterText));
    }

    partial void OnErrorTextChanged(string value)
    {
        OnPropertyChanged(nameof(ShowPlaybackErrorBar));
    }

    private void OnSnapshotChanged(object? sender, PlaybackSnapshot snapshot)
    {
        var pageEventGeneration = Volatile.Read(ref _pageEventGeneration);
        if (!_uiScheduler.CheckAccess())
        {
            try
            {
                _pageTasks.Register(
                    _uiScheduler.InvokeAsync(
                        () => HandleSnapshotUpdateAsync(snapshot, pageEventGeneration),
                        _pageEventCancellation.Token),
                    exception => ReportViewOperationFailure("更新播放页面失败", exception));
            }
            catch (OperationCanceledException) when (_pageEventCancellation.IsCancellationRequested)
            {
            }
            return;
        }

        _pageTasks.Register(
            HandleSnapshotUpdateAsync(snapshot, pageEventGeneration),
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

    private void OnCacheDecorationStateChanged(object? sender, EventArgs eventArgs)
    {
        CurrentChapterItem = _contentController.CurrentChapterItem;

        OnPropertyChanged(nameof(IsActiveCacheSelectionMode));
        OnPropertyChanged(nameof(SelectedActiveCacheChapterCount));
        OnPropertyChanged(nameof(ActiveCacheSelectionSummary));
        OnPropertyChanged(nameof(ActiveCacheStatusText));
        OnPropertyChanged(nameof(HasActiveCacheBatch));
        OnPropertyChanged(nameof(CanStartActiveCache));
    }

    private void OnInteractionStateChanged(object? sender, PlayerInteractionStateChangedEventArgs eventArgs)
    {
        if (!_isPageEventsRegistered)
        {
            return;
        }

        var pageEventGeneration = Volatile.Read(ref _pageEventGeneration);
        if (!_uiScheduler.CheckAccess())
        {
            _pageTasks.Register(
                _uiScheduler.InvokeAsync(
                    () =>
                    {
                        if (IsCurrentPageEvent(pageEventGeneration))
                        {
                            NotifyInteractionStateChanged(eventArgs);
                        }
                    },
                    _pageEventCancellation.Token),
                exception => ReportViewOperationFailure("更新滚动状态失败", exception));
            return;
        }

        if (IsCurrentPageEvent(pageEventGeneration))
        {
            NotifyInteractionStateChanged(eventArgs);
        }
    }

    private void NotifyInteractionStateChanged(PlayerInteractionStateChangedEventArgs eventArgs)
    {
        if (eventArgs.ScrollStateChanged)
        {
            OnPropertyChanged(nameof(AutoScrollState));
            OnPropertyChanged(nameof(ShouldAutoCenterCurrentSegment));
            OnPropertyChanged(nameof(ShowReturnToCurrentSegment));
        }

        if (eventArgs.CenterRequestChanged)
        {
            OnPropertyChanged(nameof(AnimateNextSegmentCenterRequest));
            OnPropertyChanged(nameof(SegmentCenterRequestVersion));
        }
    }

    private async Task HandleSnapshotUpdateAsync(PlaybackSnapshot snapshot, int pageEventGeneration)
    {
        if (!IsCurrentPageEvent(pageEventGeneration))
        {
            return;
        }

        var previousSnapshot = _lastAppliedSnapshot;
        ApplySnapshot(snapshot);

        if (string.IsNullOrWhiteSpace(snapshot.BookId))
        {
            return;
        }

        try
        {
            await EnsureContentLoadedForSnapshotAsync(
                snapshot,
                _pageEventCancellation.Token,
                pageEventGeneration);
            if (!IsCurrentPageEvent(pageEventGeneration))
            {
                return;
            }
            if (previousSnapshot.ChapterIndex != snapshot.ChapterIndex &&
                string.Equals(previousSnapshot.BookId, snapshot.BookId, StringComparison.Ordinal))
            {
                _cacheDecorationController.RequestStatusRefresh(chapterIndex: null);
            }

            if (previousSnapshot.RuleId != snapshot.RuleId ||
                previousSnapshot.SpeakSpeed != snapshot.SpeakSpeed ||
                previousSnapshot.ContentRevision != snapshot.ContentRevision)
            {
                _cacheDecorationController.RequestStatusRefresh(chapterIndex: null);
            }

            _interactionController.ApplySnapshotTransition(previousSnapshot, snapshot);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task RefreshRulesAsync(CancellationToken cancellationToken)
    {
        var rules = await _speechControlController.LoadRulesAsync(cancellationToken);
        _rules.ReplaceWith(rules, static rule => rule);
        ApplyRuleSelection(_playbackCoordinator.CurrentSnapshot.RuleId);
        OnPropertyChanged(nameof(HasRules));
    }

    private async Task EnsureContentLoadedForSnapshotAsync(
        PlaybackSnapshot snapshot,
        CancellationToken cancellationToken,
        int? expectedPageEventGeneration = null)
    {
        if (expectedPageEventGeneration is int generation && !IsCurrentPageEvent(generation))
        {
            return;
        }

        await _contentController.EnsureContentLoadedAsync(snapshot, cancellationToken);
        if (expectedPageEventGeneration is int completedGeneration &&
            !IsCurrentPageEvent(completedGeneration))
        {
            return;
        }

        SynchronizeContentProjection(includeChapterTitle: true);
        _cacheDecorationController.EnsureBookInitialized();
    }

    private bool IsCurrentPageEvent(int generation) =>
        generation == Volatile.Read(ref _pageEventGeneration) &&
        _isPageEventsRegistered &&
        !_pageEventCancellation.IsCancellationRequested;

    private async Task<PlaybackBookContent?> EnsureBookLoadedAsync(string bookId, CancellationToken cancellationToken)
    {
        var book = await _contentController.EnsureBookLoadedAsync(
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
        _contentController.ApplyPosition(snapshot.ChapterIndex, snapshot.SegmentIndex, snapshot.SegmentCount);
        var projected = _playbackProjection.Project(
            snapshot,
            _contentController.LoadedBook,
            _contentController.ResolveChapterTitle(snapshot.ChapterIndex),
            _speechControlController.DefaultSpeakSpeed);

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

        Volume = PlaybackVolume.Normalize(snapshot.Volume);

        CurrentChapterIndex = projected.ChapterIndex;
        OnPropertyChanged(nameof(CurrentChapterPosition));
        _cacheDecorationController.SetCurrentChapterIndex(projected.ChapterIndex);
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
        _cacheDecorationController.SynchronizeCatalog();
        CurrentChapterItem = _contentController.CurrentChapterItem;
        CurrentSegmentItem = _contentController.CurrentSegmentItem;
        CurrentChapterSegmentCount = _contentController.CurrentChapterSegmentCount;
        IsCurrentChapterContentLoaded = _contentController.IsChapterLoaded(CurrentChapterIndex);
        if (includeChapterTitle && !string.IsNullOrWhiteSpace(_contentController.CurrentChapterTitle))
        {
            CurrentChapterTitle = _contentController.CurrentChapterTitle;
        }

        CanGoToPreviousChapter = _contentController.CanGoToPreviousChapter;
        CanGoToNextChapter = _contentController.CanGoToNextChapter;
        CanGoToPreviousSegment = _contentController.CanGoToPreviousSegment;
        CanGoToNextSegment = _contentController.CanGoToNextSegment;
    }

    private void CloseTransientPanels()
    {
        IsRuleMenuOpen = false;
        IsSpeedMenuOpen = false;
        IsStopTimerMenuOpen = false;
        IsVolumeMenuOpen = false;
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
        StopTimerPresetMinutesText = ResolveStopTimerPresetMinutesText(snapshot);
        OnPropertyChanged(nameof(StopTimerPresetMinutesText));
        StopTimerRemainingText = FormatStopTimerRemaining(snapshot);
        UpdateStopTimerDisplayTimer(snapshot.IsActive);
    }

    private static string ResolveStopTimerPresetMinutesText(PlaybackStopTimerSnapshot snapshot)
    {
        if (!snapshot.IsActive || snapshot.Duration is not { } duration)
        {
            return string.Empty;
        }

        return duration.TotalMinutes switch
        {
            15 => "15",
            30 => "30",
            45 => "45",
            60 => "60",
            90 => "90",
            _ => string.Empty
        };
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
            SpeakSpeed > 0 ? SpeakSpeed : _speechControlController.DefaultSpeakSpeed);
    }

    private async Task ApplySpeakSpeedChangeAsync(int parsedSpeed, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _speechControlController.ApplySpeakSpeedAsync(parsedSpeed, cancellationToken);
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
