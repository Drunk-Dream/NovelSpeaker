namespace NovelSpeaker.App.Player;

public interface IPlayerAutoScrollCoordinator
{
    bool ShouldAutoCenter { get; }

    bool ShowReturnToCurrentSegment { get; }

    int PendingRestoreVersion { get; }

    event EventHandler? StateChanged;

    void NotifyUserScrollInput();

    void BeginScrollbarDrag();

    void EndScrollbarDrag();

    void BeginProgrammaticScroll();

    void EndProgrammaticScroll();

    void ReturnToCurrentSegment();

    void ResetForChapterChange();

    void ResetForPageLeave();
}
