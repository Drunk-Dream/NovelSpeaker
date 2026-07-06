namespace NovelSpeaker.App.Navigation;

public sealed record PlayerNavigationRequest(
    string BookId,
    PlayerNavigationMode Mode = PlayerNavigationMode.OpenPaused,
    int? ChapterIndex = null,
    int? SegmentIndex = null);

public enum PlayerNavigationMode
{
    OpenPaused = 0,
    ReturnToCurrentSession = 1
}
