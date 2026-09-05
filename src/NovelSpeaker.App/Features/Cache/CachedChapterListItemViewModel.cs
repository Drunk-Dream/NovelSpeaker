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
        string completenessText,
        bool isExportable = false,
        string exportAccessibilityText = "不可导出",
        string exportToolTip = "当前章节无法导出。",
        bool isSelected = false)
    {
        BookId = bookId;
        ChapterIndex = chapterIndex;
        OrderText = orderText;
        Title = title;
        CacheSizeText = cacheSizeText;
        EntryCountText = entryCountText;
        CompletenessText = completenessText;
        IsExportable = isExportable;
        ExportAccessibilityText = exportAccessibilityText;
        ExportToolTip = exportToolTip;
        IsSelected = isSelected;
    }

    public string BookId { get; }

    public int ChapterIndex { get; }

    public string OrderText { get; }

    public string Title { get; }

    public string CacheSizeText { get; }

    public string EntryCountText { get; }

    public string CompletenessText { get; }

    public bool IsExportable { get; }

    public string ExportAccessibilityText { get; }

    public string ExportToolTip { get; }

    public string AutomationName =>
        $"{OrderText}，{Title}，{CacheSizeText}，{CompletenessText}，{ExportAccessibilityText}" +
        (IsSelected ? "，已选择" : string.Empty);

    public bool IsSelected { get; }

    public CachedChapterListItemViewModel WithSelection(bool isSelected) =>
        new(
            BookId,
            ChapterIndex,
            OrderText,
            Title,
            CacheSizeText,
            EntryCountText,
            CompletenessText,
            IsExportable,
            ExportAccessibilityText,
            ExportToolTip,
            isSelected);
}
