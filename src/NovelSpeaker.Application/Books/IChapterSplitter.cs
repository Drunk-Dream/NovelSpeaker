using NovelSpeaker.Domain.Books;

namespace NovelSpeaker.Application.Books;

/// <summary>
/// Splits normalized TXT content into chapters using enabled database rules.
/// </summary>
public interface IChapterSplitter
{
    IReadOnlyList<BookImportChapter> Split(
        string normalizedText,
        IReadOnlyList<ChapterRule> rules);
}
