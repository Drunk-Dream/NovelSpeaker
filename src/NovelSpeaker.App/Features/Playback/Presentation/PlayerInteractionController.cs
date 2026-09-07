using NovelSpeaker.Application.Playback;
using NovelSpeaker.App.Features.Playback.Scrolling;

namespace NovelSpeaker.App.Features.Playback.Presentation;

/// <summary>
/// Owns page-scoped playback navigation and auto-scroll interaction state.
/// Playback position itself remains owned by <see cref="IPlaybackSession"/>.
/// </summary>
internal sealed class PlayerInteractionController
{
    private readonly IPlaybackSession _playbackSession;
    private readonly IPlayerAutoScrollCoordinator _autoScrollCoordinator;
    private PlayerAutoScrollState _lastAppliedAutoScrollState;
    private bool _suppressNextStateDrivenCenterRequest;
    private bool _isActive;
    private int _segmentCenterRequestVersion;
    private bool _animateNextSegmentCenterRequest;

    public PlayerInteractionController(
        IPlaybackSession playbackSession,
        IPlayerAutoScrollCoordinator autoScrollCoordinator)
    {
        _playbackSession = playbackSession ?? throw new ArgumentNullException(nameof(playbackSession));
        _autoScrollCoordinator = autoScrollCoordinator ?? throw new ArgumentNullException(nameof(autoScrollCoordinator));
        _lastAppliedAutoScrollState = autoScrollCoordinator.State;
    }

    public event EventHandler<PlayerInteractionStateChangedEventArgs>? StateChanged;

    public PlayerAutoScrollState AutoScrollState => _autoScrollCoordinator.State;

    public bool ShouldAutoCenterCurrentSegment => _autoScrollCoordinator.ShouldAutoCenter;

    public bool ShowReturnToCurrentSegment => _autoScrollCoordinator.ShowReturnToCurrentSegment;

    public int SegmentCenterRequestVersion => _segmentCenterRequestVersion;

    public bool AnimateNextSegmentCenterRequest => _animateNextSegmentCenterRequest;

    public void Activate()
    {
        if (_isActive)
        {
            return;
        }

        _autoScrollCoordinator.StateChanged += OnAutoScrollStateChanged;
        _lastAppliedAutoScrollState = _autoScrollCoordinator.State;
        _isActive = true;
        RaiseStateChanged(scrollStateChanged: true, centerRequestChanged: false);
    }

    public void Deactivate()
    {
        if (_isActive)
        {
            _autoScrollCoordinator.StateChanged -= OnAutoScrollStateChanged;
            _isActive = false;
        }

        _suppressNextStateDrivenCenterRequest = false;
        _autoScrollCoordinator.ResetForPageLeave();
        _lastAppliedAutoScrollState = _autoScrollCoordinator.State;
        RaiseStateChanged(scrollStateChanged: true, centerRequestChanged: false);
    }

    public void NotifyUserScrollInput() => _autoScrollCoordinator.NotifyUserScrollInput();

    public void NotifyPassiveSegmentScrollChange() => _autoScrollCoordinator.NotifyPassiveScrollChange();

    public void NotifyScrollbarDragStarted() => _autoScrollCoordinator.BeginScrollbarDrag();

    public void NotifyScrollbarDragCompleted() => _autoScrollCoordinator.EndScrollbarDrag();

    public void NotifyProgrammaticScrollStarted() => _autoScrollCoordinator.BeginProgrammaticScroll();

    public void NotifyProgrammaticScrollCompleted() => _autoScrollCoordinator.EndProgrammaticScroll();

    public void ResumeAutoCenterAndRequest(bool animate)
    {
        ResumeAutoCenterForExplicitNavigation();
        RequestCurrentSegmentCentering(animate);
    }

    public void ResumeAutoCenterForExplicitNavigation()
    {
        if (_autoScrollCoordinator.State == PlayerAutoScrollState.AutoCentering)
        {
            return;
        }

        _suppressNextStateDrivenCenterRequest = true;
        _autoScrollCoordinator.ResumeAutoCenter();
    }

    public void ApplySnapshotTransition(PlaybackSnapshot previousSnapshot, PlaybackSnapshot snapshot)
    {
        if (_autoScrollCoordinator.ShouldAutoCenter &&
            ShouldAnimateCenteringForSnapshotUpdate(previousSnapshot, snapshot))
        {
            RequestCurrentSegmentCentering(animate: true);
        }
    }

    public async Task JumpToChapterAsync(int chapterIndex, CancellationToken cancellationToken)
    {
        ResumeAutoCenterForExplicitNavigation();
        await _playbackSession.JumpToChapterAsync(chapterIndex, cancellationToken);
    }

    public async Task JumpToSegmentAsync(int chapterIndex, int segmentIndex, CancellationToken cancellationToken)
    {
        ResumeAutoCenterForExplicitNavigation();
        await _playbackSession.JumpToSegmentAsync(chapterIndex, segmentIndex, cancellationToken);
    }

    public async Task PreviousChapterAsync(CancellationToken cancellationToken)
    {
        ResumeAutoCenterForExplicitNavigation();
        await _playbackSession.PreviousChapterAsync(cancellationToken);
    }

    public async Task NextChapterAsync(CancellationToken cancellationToken)
    {
        ResumeAutoCenterForExplicitNavigation();
        await _playbackSession.NextChapterAsync(cancellationToken);
    }

    public async Task PreviousSegmentAsync(CancellationToken cancellationToken)
    {
        ResumeAutoCenterForExplicitNavigation();
        await _playbackSession.PreviousSegmentAsync(cancellationToken);
    }

    public async Task NextSegmentAsync(CancellationToken cancellationToken)
    {
        ResumeAutoCenterForExplicitNavigation();
        await _playbackSession.NextSegmentAsync(cancellationToken);
    }

    private void OnAutoScrollStateChanged(object? sender, EventArgs eventArgs)
    {
        if (!_isActive)
        {
            return;
        }

        var currentState = _autoScrollCoordinator.State;
        if (currentState == PlayerAutoScrollState.AutoCentering &&
            _lastAppliedAutoScrollState != PlayerAutoScrollState.AutoCentering)
        {
            if (_suppressNextStateDrivenCenterRequest)
            {
                _suppressNextStateDrivenCenterRequest = false;
            }
            else
            {
                RequestCurrentSegmentCentering(animate: false);
            }
        }
        else if (currentState != PlayerAutoScrollState.AutoCentering)
        {
            _suppressNextStateDrivenCenterRequest = false;
        }

        _lastAppliedAutoScrollState = currentState;
        RaiseStateChanged(scrollStateChanged: true, centerRequestChanged: false);
    }

    private void RequestCurrentSegmentCentering(bool animate)
    {
        _animateNextSegmentCenterRequest = animate;
        _segmentCenterRequestVersion++;
        RaiseStateChanged(scrollStateChanged: false, centerRequestChanged: true);
    }

    private void RaiseStateChanged(bool scrollStateChanged, bool centerRequestChanged) =>
        StateChanged?.Invoke(
            this,
            new PlayerInteractionStateChangedEventArgs(scrollStateChanged, centerRequestChanged));

    private static bool ShouldAnimateCenteringForSnapshotUpdate(
        PlaybackSnapshot previousSnapshot,
        PlaybackSnapshot snapshot)
    {
        if (!string.Equals(previousSnapshot.BookId, snapshot.BookId, StringComparison.Ordinal))
        {
            return !string.IsNullOrWhiteSpace(snapshot.BookId);
        }

        return previousSnapshot.ChapterIndex != snapshot.ChapterIndex ||
               previousSnapshot.SegmentIndex != snapshot.SegmentIndex;
    }
}

internal sealed record PlayerInteractionStateChangedEventArgs(
    bool ScrollStateChanged,
    bool CenterRequestChanged);
