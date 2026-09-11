namespace NovelSpeaker.Application.Books;

/// <summary>
/// Book and chapter metadata required to locate runtime playback content.
/// </summary>
public sealed record PlaybackBookMetadata(
    string BookId,
    string Title,
    string? Author,
    IReadOnlyList<PlaybackChapterSummaryMetadata> Chapters);

/// <summary>
/// Book identity metadata used when a caller already owns the chapter catalog.
/// </summary>
public sealed record PlaybackBookHeader(
    string BookId,
    string Title,
    string? Author);

/// <summary>
/// Chapter metadata used by book-level playback navigation.
/// </summary>
public sealed record PlaybackChapterSummaryMetadata(
    int ChapterIndex,
    string Title);

/// <summary>
/// Persisted chapter metadata. Text remains owned by the book content store.
/// </summary>
public sealed record PlaybackChapterMetadata(
    int ChapterIndex,
    string Title,
    string StoredFilePath,
    int StartOffset,
    int Length,
    string? ChapterId = null);
