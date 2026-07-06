using CommunityToolkit.Mvvm.ComponentModel;

namespace NovelSpeaker.App.ViewModels;

public sealed partial class CachedBookListItemViewModel : ObservableObject
{
    public CachedBookListItemViewModel(
        string bookId,
        string title,
        string? author,
        string cacheSizeText,
        string chapterCountText)
    {
        BookId = bookId;
        Title = title;
        Author = string.IsNullOrWhiteSpace(author) ? "未知作者" : author.Trim();
        CacheSizeText = cacheSizeText;
        ChapterCountText = chapterCountText;
        AutomationName = $"{Title}，{Author}，{CacheSizeText}，{ChapterCountText}";
    }

    public string BookId { get; }

    public string Title { get; }

    public string Author { get; }

    public string CacheSizeText { get; }

    public string ChapterCountText { get; }

    public string AutomationName { get; }

    [ObservableProperty]
    private bool isSelected;
}
