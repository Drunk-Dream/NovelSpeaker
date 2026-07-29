using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.App.Shared.Presentation;
using NovelSpeaker.App.Shared.Presentation.Platform;
using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.App.Features.Playback.Components;

namespace NovelSpeaker.App.Desktop.MiniPlayer;

public sealed partial class MiniPlayerViewModel :
    ObservableObject,
    ISegmentProgressInteractionTarget,
    IMiniPlayerPlacementPersistence,
    IAsyncDisposable
{
    internal static readonly TimeSpan PlacementSaveDelay = TimeSpan.FromMilliseconds(300);

    private readonly object _persistenceSyncRoot = new();
    private readonly IPlaybackSession _playbackSession;
    private readonly IAppSettingsService _settingsService;
    private readonly IUiScheduler _uiScheduler;
    private readonly IAppFeedbackService? _feedbackService;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<MiniPlayerViewModel> _logger;
    private readonly OwnedTaskRegistry _ownedTasks = new();
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private CancellationTokenSource? _pendingSaveCancellation;
    private double? _pendingLeft;
    private double? _pendingTop;
    private bool _hasPendingPlacementChanges;
    private bool _disposed;

    public MiniPlayerViewModel(
        IPlaybackSession playbackSession,
        IAppSettingsService settingsService,
        IUiScheduler uiScheduler,
        ILogger<MiniPlayerViewModel> logger,
        TimeProvider? timeProvider = null,
        IAppFeedbackService? feedbackService = null)
    {
        _playbackSession = playbackSession;
        _settingsService = settingsService;
        _uiScheduler = uiScheduler;
        _logger = logger;
        _feedbackService = feedbackService;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _pendingLeft = settingsService.Current.MiniPlayerLeft;
        _pendingTop = settingsService.Current.MiniPlayerTop;
        isTopmost = settingsService.Current.MiniPlayerTopmost;
        ApplySnapshot(playbackSession.CurrentSnapshot);
        playbackSession.SnapshotChanged += OnSnapshotChanged;
    }

    public event EventHandler? RestoreRequested;

    public double? SavedLeft => _settingsService.Current.MiniPlayerLeft;

    public double? SavedTop => _settingsService.Current.MiniPlayerTop;

    public bool HasPlaybackContext => !string.IsNullOrWhiteSpace(_playbackSession.CurrentSnapshot.BookId);

    public bool CanTogglePlayback =>
        CurrentPlaybackState is PlaybackState.Playing or PlaybackState.Paused;

    public string PlaybackActionText =>
        CurrentPlaybackState == PlaybackState.Playing ? "暂停" : "播放";

    public string VolumePercentText => $"{Math.Round(Volume * 100d):0}%";

    public string VolumeButtonAutomationName => $"播放音量 {VolumePercentText}";

    public string TopmostActionText => IsTopmost ? "取消置顶" : "置顶";

    public string DisplayedSegmentCounterText
    {
        get
        {
            var segmentIndex = IsSegmentProgressDragging
                ? (int)Math.Round(SegmentProgressPreviewValue)
                : CurrentSegmentIndex;
            return SegmentCount > 0 && segmentIndex >= 0
                ? $"{segmentIndex + 1} / {SegmentCount}"
                : "尚未定位段落";
        }
    }

    public double SegmentProgressMaximum => Math.Max(SegmentCount - 1, 0);

    internal CancellationToken LifetimeCancellationToken => _lifetimeCancellation.Token;

    [ObservableProperty]
    private string bookTitle = "未打开书籍";

    [ObservableProperty]
    private string chapterTitle = "尚未定位章节";

    [ObservableProperty]
    private PlaybackState currentPlaybackState = PlaybackState.Idle;

    [ObservableProperty]
    private bool canGoToPreviousSegment;

    [ObservableProperty]
    private bool canGoToNextSegment;

    [ObservableProperty]
    private double segmentProgressValue;

    [ObservableProperty]
    private double segmentProgressPreviewValue;

    [ObservableProperty]
    private bool isSegmentProgressDragging;

    [ObservableProperty]
    private int currentChapterIndex = -1;

    [ObservableProperty]
    private int currentSegmentIndex = -1;

    [ObservableProperty]
    private int segmentCount;

    [ObservableProperty]
    private bool isTopmost;

    [ObservableProperty]
    private bool isVolumeMenuOpen;

    [ObservableProperty]
    private double volume = PlaybackVolume.Default;

    partial void OnIsTopmostChanged(bool value)
    {
        OnPropertyChanged(nameof(TopmostActionText));
        SchedulePlacementSave();
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task TogglePlaybackAsync(CancellationToken cancellationToken)
    {
        if (CurrentPlaybackState == PlaybackState.Playing)
        {
            await _playbackSession.PauseAsync(cancellationToken);
        }
        else if (CurrentPlaybackState == PlaybackState.Paused)
        {
            await _playbackSession.ResumeAsync(cancellationToken);
        }
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private Task PreviousChapterAsync(CancellationToken cancellationToken) =>
        _playbackSession.PreviousChapterAsync(cancellationToken);

    [RelayCommand(AllowConcurrentExecutions = false)]
    private Task NextChapterAsync(CancellationToken cancellationToken) =>
        _playbackSession.NextChapterAsync(cancellationToken);

    [RelayCommand(AllowConcurrentExecutions = false)]
    private Task PreviousSegmentAsync(CancellationToken cancellationToken) =>
        _playbackSession.PreviousSegmentAsync(cancellationToken);

    [RelayCommand(AllowConcurrentExecutions = false)]
    private Task NextSegmentAsync(CancellationToken cancellationToken) =>
        _playbackSession.NextSegmentAsync(cancellationToken);

    [RelayCommand]
    private void RestoreMainWindow() => RestoreRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void ToggleTopmost() => IsTopmost = !IsTopmost;

    [RelayCommand]
    private void ToggleVolumeMenu() => IsVolumeMenuOpen = !IsVolumeMenuOpen;

    public void RequestRestore() => RestoreRequested?.Invoke(this, EventArgs.Empty);

    public void BeginSegmentProgressInteraction()
    {
        if (SegmentCount <= 0)
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
        if (SegmentCount <= 0 || CurrentChapterIndex < 0)
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
            return;
        }

        await _playbackSession.JumpToSegmentAsync(
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

    internal void ReportProgressOperationFailure(Exception exception)
    {
        if (_feedbackService is not null)
        {
            _feedbackService.ShowProjectedNotification("跳转段落失败", _feedbackService.Project(exception));
            return;
        }

        _logger.LogError(
            exception,
            "Mini-player segment progress operation failed with {FailureType}.",
            exception.GetType().Name);
    }

    public void UpdateWindowPosition(double left, double top)
    {
        if (!double.IsFinite(left) || !double.IsFinite(top))
        {
            return;
        }

        lock (_persistenceSyncRoot)
        {
            _pendingLeft = left;
            _pendingTop = top;
            _hasPendingPlacementChanges = true;
        }

        SchedulePlacementSave();
    }

    public async Task FlushPlacementAsync(CancellationToken cancellationToken)
    {
        CancellationTokenSource? pendingCancellation;
        lock (_persistenceSyncRoot)
        {
            pendingCancellation = _pendingSaveCancellation;
            _pendingSaveCancellation = null;
        }

        pendingCancellation?.Cancel();
        pendingCancellation?.Dispose();
        await SavePlacementAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _playbackSession.SnapshotChanged -= OnSnapshotChanged;
        await FlushPlacementAsync(CancellationToken.None).ConfigureAwait(false);
        _lifetimeCancellation.Cancel();
        await _ownedTasks.WaitForCompletionAsync().ConfigureAwait(false);
        _lifetimeCancellation.Dispose();
    }

    private void OnSnapshotChanged(object? sender, PlaybackSnapshot snapshot)
    {
        if (_uiScheduler.CheckAccess())
        {
            ApplySnapshot(snapshot);
            return;
        }

        _ownedTasks.Register(
            _uiScheduler.InvokeAsync(() => ApplySnapshot(snapshot), _lifetimeCancellation.Token),
            exception => _logger.LogError(
                "Mini-player snapshot projection failed with {FailureType}.",
                exception.GetType().Name));
    }

    private void ApplySnapshot(PlaybackSnapshot snapshot)
    {
        BookTitle = string.IsNullOrWhiteSpace(snapshot.BookTitle) ? "未打开书籍" : snapshot.BookTitle;
        ChapterTitle = string.IsNullOrWhiteSpace(snapshot.ChapterTitle) ? "尚未定位章节" : snapshot.ChapterTitle;
        CurrentPlaybackState = snapshot.State;
        Volume = PlaybackVolume.Normalize(snapshot.Volume);
        CurrentChapterIndex = snapshot.ChapterIndex;
        CurrentSegmentIndex = snapshot.SegmentIndex;
        SegmentCount = snapshot.SegmentCount;
        CanGoToPreviousSegment = !string.IsNullOrWhiteSpace(snapshot.BookId) && snapshot.SegmentIndex > 0;
        CanGoToNextSegment = !string.IsNullOrWhiteSpace(snapshot.BookId) &&
                             snapshot.SegmentIndex + 1 < snapshot.SegmentCount;
        if (!IsSegmentProgressDragging)
        {
            SegmentProgressValue = NormalizeSegmentProgressValue(snapshot.SegmentIndex);
            SegmentProgressPreviewValue = SegmentProgressValue;
        }
        OnPropertyChanged(nameof(HasPlaybackContext));
        OnPropertyChanged(nameof(CanTogglePlayback));
        OnPropertyChanged(nameof(PlaybackActionText));
        OnPropertyChanged(nameof(DisplayedSegmentCounterText));
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
        _playbackSession.SetVolume(normalized);
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

    partial void OnSegmentCountChanged(int value)
    {
        OnPropertyChanged(nameof(SegmentProgressMaximum));
        OnPropertyChanged(nameof(DisplayedSegmentCounterText));
        if (!IsSegmentProgressDragging)
        {
            SegmentProgressValue = NormalizeSegmentProgressValue(CurrentSegmentIndex);
            SegmentProgressPreviewValue = SegmentProgressValue;
        }
    }

    partial void OnIsSegmentProgressDraggingChanged(bool value)
    {
        OnPropertyChanged(nameof(DisplayedSegmentCounterText));
    }

    private double NormalizeSegmentProgressValue(double value)
    {
        if (SegmentCount <= 0)
        {
            return 0d;
        }

        return Math.Clamp(Math.Round(value), 0d, SegmentProgressMaximum);
    }

    private void SchedulePlacementSave()
    {
        CancellationTokenSource saveCancellation;
        Task saveTask;
        lock (_persistenceSyncRoot)
        {
            if (_disposed)
            {
                return;
            }

            _hasPendingPlacementChanges = true;
            _pendingSaveCancellation?.Cancel();
            _pendingSaveCancellation?.Dispose();
            saveCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
            _pendingSaveCancellation = saveCancellation;
            saveTask = SavePlacementAfterDelayAsync(saveCancellation);
        }

        _ownedTasks.Register(
            saveTask,
            exception => _logger.LogError(
                "Mini-player placement save failed with {FailureType}.",
                exception.GetType().Name));
    }

    private async Task SavePlacementAfterDelayAsync(CancellationTokenSource saveCancellation)
    {
        try
        {
            await Task.Delay(
                PlacementSaveDelay,
                _timeProvider,
                saveCancellation.Token).ConfigureAwait(false);
            await SavePlacementAsync(saveCancellation.Token).ConfigureAwait(false);
        }
        finally
        {
            lock (_persistenceSyncRoot)
            {
                if (ReferenceEquals(_pendingSaveCancellation, saveCancellation))
                {
                    _pendingSaveCancellation = null;
                }
            }

            saveCancellation.Dispose();
        }
    }

    private async Task SavePlacementAsync(CancellationToken cancellationToken)
    {
        double? left;
        double? top;
        bool topmost;
        lock (_persistenceSyncRoot)
        {
            if (!_hasPendingPlacementChanges)
            {
                return;
            }

            left = _pendingLeft;
            top = _pendingTop;
            topmost = IsTopmost;
        }

        await _settingsService.UpdateAsync(
            new AppSettingsUpdate
            {
                MiniPlayerLeft = left,
                ClearMiniPlayerLeft = left is null,
                MiniPlayerTop = top,
                ClearMiniPlayerTop = top is null,
                MiniPlayerTopmost = topmost
            },
            cancellationToken).ConfigureAwait(false);

        lock (_persistenceSyncRoot)
        {
            if (_pendingLeft == left && _pendingTop == top && IsTopmost == topmost)
            {
                _hasPendingPlacementChanges = false;
            }
        }
    }
}
