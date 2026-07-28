using NovelSpeaker.Domain.Books;

namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Represents one imported book projected into playback-ready chapters and segments.
/// </summary>
public sealed record PlaybackBookContent(
    string BookId,
    string BookTitle,
    IReadOnlyList<PlaybackChapterContent> Chapters,
    string? BookAuthor = null);

/// <summary>
/// Describes whether runtime content has been assembled for a chapter.
/// </summary>
public enum PlaybackChapterLoadState
{
    Unloaded,
    LoadedEmpty,
    Loaded,
    Failed
}

/// <summary>
/// Represents one playback-ready chapter and its explicit runtime loading state.
/// </summary>
public sealed record PlaybackChapterContent
{
    private PlaybackChapterContent(
        int chapterIndex,
        string title,
        IReadOnlyList<SpeechSegment> segments,
        PlaybackChapterLoadState loadState,
        string? chapterId)
    {
        ChapterIndex = chapterIndex;
        Title = title;
        Segments = segments;
        LoadState = loadState;
        ChapterId = chapterId;
    }

    public int ChapterIndex { get; }

    public string Title { get; }

    public IReadOnlyList<SpeechSegment> Segments { get; }

    public PlaybackChapterLoadState LoadState { get; }

    public string? ChapterId { get; }

    public static PlaybackChapterContent Unloaded(int chapterIndex, string title, string? chapterId = null) =>
        new(chapterIndex, title, [], PlaybackChapterLoadState.Unloaded, chapterId);

    public static PlaybackChapterContent FromLoaded(
        int chapterIndex,
        string title,
        IReadOnlyList<SpeechSegment> segments,
        string? chapterId = null)
    {
        ArgumentNullException.ThrowIfNull(segments);
        return new PlaybackChapterContent(
            chapterIndex,
            title,
            segments,
            segments.Count == 0 ? PlaybackChapterLoadState.LoadedEmpty : PlaybackChapterLoadState.Loaded,
            chapterId);
    }

    public static PlaybackChapterContent Failed(int chapterIndex, string title, string? chapterId = null) =>
        new(chapterIndex, title, [], PlaybackChapterLoadState.Failed, chapterId);
}
