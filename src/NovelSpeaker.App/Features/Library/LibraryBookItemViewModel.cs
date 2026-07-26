using CommunityToolkit.Mvvm.ComponentModel;
using NovelSpeaker.App.Shared.Presentation.Books;

namespace NovelSpeaker.App.Features.Library;

public sealed partial class LibraryBookItemViewModel : ObservableObject
{
    private readonly string _normalizedSearchText;

    public LibraryBookItemViewModel(
        string bookId,
        string title,
        string displayAuthor,
        string currentChapterTitle,
        string remainingChapterText,
        double progressRatio,
        bool hasReadingProgress,
        string? lastPlayedAt,
        GeneratedBookCover cover,
        bool canDelete)
    {
        BookId = bookId;
        Title = title;
        DisplayAuthor = displayAuthor;
        CurrentChapterTitle = currentChapterTitle;
        RemainingChapterText = remainingChapterText;
        ProgressRatio = Math.Clamp(progressRatio, 0, 1);
        HasReadingProgress = hasReadingProgress;
        LastPlayedAt = lastPlayedAt;
        Cover = cover;
        CanDelete = canDelete;
        _normalizedSearchText = $"{NormalizeSearchText(title)}|{NormalizeSearchText(displayAuthor)}";
    }

    public string BookId { get; }

    public string Title { get; }

    public string DisplayAuthor { get; }

    public string CurrentChapterTitle { get; }

    public string TitleToolTip => Title;

    public string CurrentChapterToolTip => CurrentChapterTitle;

    public string RemainingChapterText { get; }

    public double ProgressRatio { get; }

    public bool HasReadingProgress { get; }

    public string? LastPlayedAt { get; }

    public GeneratedBookCover Cover { get; }

    public string SortTitleKey => Cover.NormalizedTitleKey;

    public string ProgressAutomationText => HasReadingProgress
        ? $"总体进度 {Math.Round(ProgressRatio * 100d, 0):0}%"
        : "尚无阅读进度";

    public string AutomationName =>
        $"打开《{Title}》，作者 {DisplayAuthor}，当前章节 {CurrentChapterTitle}，{RemainingChapterText}，{ProgressAutomationText}";

    public string MoreActionsAutomationName => $"《{Title}》的更多操作";

    [ObservableProperty]
    private bool canDelete;

    public bool MatchesSearch(string normalizedSearchTerm)
    {
        return string.IsNullOrEmpty(normalizedSearchTerm) ||
            _normalizedSearchText.Contains(normalizedSearchTerm, StringComparison.Ordinal);
    }

    internal static string NormalizeSearchText(string? value)
    {
        return string.Join(
            ' ',
            (value ?? string.Empty)
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToUpperInvariant();
    }
}
