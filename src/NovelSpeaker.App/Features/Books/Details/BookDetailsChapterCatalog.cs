using NovelSpeaker.Application.Books;
using NovelSpeaker.App.Shared.Presentation;

namespace NovelSpeaker.App.Features.Books.Details;

/// <summary>
/// Immutable chapter catalog with O(1) chapter-index lookup for details-page projections.
/// </summary>
internal sealed class BookDetailsChapterCatalog
{
    private readonly IndexedCatalog<BookChapterSummary> _catalog;

    public BookDetailsChapterCatalog(IReadOnlyList<BookChapterSummary> chapters)
    {
        ArgumentNullException.ThrowIfNull(chapters);
        _catalog = new IndexedCatalog<BookChapterSummary>(
            chapters,
            static chapter => chapter.ChapterIndex);
    }

    public IReadOnlyList<BookChapterSummary> Items => _catalog.Items;

    public int Count => _catalog.Count;

    public BookChapterSummary this[int position] => _catalog[position];

    public bool TryGet(int chapterIndex, out BookChapterSummary chapter) =>
        _catalog.TryGet(chapterIndex, out chapter!);

    public bool TryGetPosition(int chapterIndex, out int position) =>
        _catalog.TryGetPosition(chapterIndex, out position);

    public bool Contains(int chapterIndex) => _catalog.TryGetPosition(chapterIndex, out _);

    public IReadOnlyList<BookChapterSummary> Slice(int start, int count) =>
        _catalog.Slice(start, count);
}
