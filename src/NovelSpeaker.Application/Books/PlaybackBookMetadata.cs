namespace NovelSpeaker.Application.Books;

/// <summary>
/// Book and chapter metadata required to locate runtime playback content.
/// </summary>
public sealed record PlaybackBookMetadata(
    string BookId,
    string Title,
    string? Author,
    IReadOnlyList<PlaybackChapterSummaryMetadata> Chapters,
    ActiveSourceContext? SourceContext = null);

/// <summary>
/// Book identity metadata used when a caller already owns the chapter catalog.
/// </summary>
public sealed record PlaybackBookHeader(
    string BookId,
    string Title,
    string? Author,
    ActiveSourceContext? SourceContext = null);

/// <summary>
/// Chapter metadata used by book-level playback navigation.
/// </summary>
public sealed record PlaybackChapterSummaryMetadata(
    int ChapterIndex,
    string Title,
    string? ChapterId = null);

/// <summary>
/// Source catalog entry. Storage paths and typed content ranges remain in Infrastructure.
/// </summary>
public sealed record PlaybackChapterMetadata(
    int ChapterIndex,
    string Title,
    string SourceId,
    string ChapterId,
    ActiveSourceContext? SourceContext = null);
