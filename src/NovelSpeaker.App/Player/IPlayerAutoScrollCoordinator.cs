namespace NovelSpeaker.App.Player;

public interface IPlayerAutoScrollCoordinator
{
    PlayerAutoScrollState State { get; }

    bool ShouldAutoCenter { get; }

    bool ShowReturnToCurrentSegment { get; }

    int PendingRestoreVersion { get; }

    event EventHandler? StateChanged;

    void NotifyUserScrollInput();

    void NotifyPassiveScrollChange();

    void BeginScrollbarDrag();

    void EndScrollbarDrag();

    void BeginProgrammaticScroll();

    void EndProgrammaticScroll();

    void ResumeAutoCenter();

    void ResetForPageLeave();
}
