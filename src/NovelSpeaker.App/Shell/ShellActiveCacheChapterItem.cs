namespace NovelSpeaker.App.Shell;

/// <summary>
/// Immutable shell projection of one chapter in the process-owned active-cache snapshot.
/// </summary>
public sealed record ShellActiveCacheChapterItem(
    int ChapterIndex,
    string Title,
    string StatusText,
    bool IsCurrent,
    bool IsCompleted,
    bool IsFailed);
