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
/// Book CurrentCatalog entry. The binding identifies the producer of this snapshot;
/// storage paths and typed content ranges remain in Infrastructure.
/// </summary>
public sealed record PlaybackChapterMetadata(
    string BookId,
    int ChapterIndex,
    string Title,
    string SourceBindingId,
    string ChapterId,
    ActiveSourceContext? SourceContext = null);
