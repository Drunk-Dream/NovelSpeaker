namespace NovelSpeaker.App.Features.Cache;

public sealed class CachedChapterListItemViewModel
{
    public CachedChapterListItemViewModel(
        string bookId,
        int chapterIndex,
        string orderText,
        string title,
        string cacheSizeText,
        string entryCountText,
        string completenessText)
    {
        BookId = bookId;
        ChapterIndex = chapterIndex;
        OrderText = orderText;
        Title = title;
        CacheSizeText = cacheSizeText;
        EntryCountText = entryCountText;
        CompletenessText = completenessText;
        AutomationName = $"{orderText}，{title}，{cacheSizeText}，{completenessText}";
    }

    public string BookId { get; }

    public int ChapterIndex { get; }

    public string OrderText { get; }

    public string Title { get; }

    public string CacheSizeText { get; }

    public string EntryCountText { get; }

    public string CompletenessText { get; }

    public string AutomationName { get; }
}
