using CommunityToolkit.Mvvm.ComponentModel;

namespace NovelSpeaker.App.Features.Cache;

public sealed partial class CachedChapterListItemViewModel : ObservableObject
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
        string exportToolTip = "当前章节无法导出。")
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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AutomationName))]
    private bool isSelected;
}
