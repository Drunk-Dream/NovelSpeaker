namespace NovelSpeaker.App.Features.Cache;

public sealed class CachedBookListItemViewModel
{
    public CachedBookListItemViewModel(
        string bookId,
        string title,
        string? author,
        string cacheSizeText,
        string chapterCountText,
        bool isSelected = false,
        long totalSizeBytes = 0)
    {
        BookId = bookId;
        Title = title;
        Author = string.IsNullOrWhiteSpace(author) ? "未知作者" : author.Trim();
        CacheSizeText = cacheSizeText;
        ChapterCountText = chapterCountText;
        TotalSizeBytes = totalSizeBytes;
        AutomationName = $"{Title}，{Author}，{CacheSizeText}，{ChapterCountText}";
        IsSelected = isSelected;
    }

    public string BookId { get; }

    public string Title { get; }

    public string Author { get; }

    public string CacheSizeText { get; }

    public string ChapterCountText { get; }

    public long TotalSizeBytes { get; }

    public string AutomationName { get; }

    public bool IsSelected { get; }

    public CachedBookListItemViewModel WithSelection(bool isSelected) =>
        new(BookId, Title, Author, CacheSizeText, ChapterCountText, isSelected, TotalSizeBytes);
}
