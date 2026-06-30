using CommunityToolkit.Mvvm.ComponentModel;
using NovelSpeaker.App.Library;
using System.Windows.Media;

namespace NovelSpeaker.App.ViewModels;

public sealed partial class LibraryBookItemViewModel : ObservableObject
{
    public LibraryBookItemViewModel(
        string id,
        string title,
        string? author,
        string currentChapterTitle,
        string importedAt,
        string? lastPlayedAt = null)
        : this(
            id,
            title,
            string.IsNullOrWhiteSpace(author) ? "未知作者" : author.Trim(),
            currentChapterTitle,
            string.Empty,
            0,
            false,
            lastPlayedAt,
            CreateCompatibilityCover(title),
            canDelete: true)
    {
        ImportedAt = importedAt;
    }

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
        ImportedAt = string.Empty;
        Title = title;
        DisplayAuthor = displayAuthor;
        CurrentChapterTitle = currentChapterTitle;
        RemainingChapterText = remainingChapterText;
        ProgressRatio = Math.Clamp(progressRatio, 0, 1);
        HasReadingProgress = hasReadingProgress;
        LastPlayedAt = lastPlayedAt;
        Cover = cover;
        CanDelete = canDelete;
        NormalizedSearchText = $"{NormalizeSearchText(title)}|{NormalizeSearchText(displayAuthor)}";
    }

    public string BookId { get; }

    public string Id => BookId;

    public string ImportedAt { get; }

    public string Title { get; }

    public string DisplayAuthor { get; }

    public string CurrentChapterTitle { get; }

    public string RemainingChapterText { get; }

    public double ProgressRatio { get; }

    public bool HasReadingProgress { get; }

    public string? LastPlayedAt { get; }

    public GeneratedBookCover Cover { get; }

    public string SortTitleKey => Cover.NormalizedTitleKey;

    public string NormalizedSearchText { get; }

    [ObservableProperty]
    private bool canDelete;

    public bool MatchesSearch(string normalizedSearchTerm)
    {
        return string.IsNullOrEmpty(normalizedSearchTerm) ||
            NormalizedSearchText.Contains(normalizedSearchTerm, StringComparison.Ordinal);
    }

    internal static string NormalizeSearchText(string? value)
    {
        return string.Join(
            ' ',
            (value ?? string.Empty)
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToUpperInvariant();
    }

    private static GeneratedBookCover CreateCompatibilityCover(string title)
    {
        return new GeneratedBookCover(
            BookCoverGenerator.NormalizeTitleKey(title),
            BookCoverGenerator.BuildDisplayLines(title),
            palettePresetId: 4,
            decorationPresetId: 0,
            foregroundTone: BookCoverForegroundTone.Dark,
            startColor: Color.FromRgb(226, 232, 240),
            endColor: Color.FromRgb(148, 163, 184),
            accentColor: Color.FromArgb(100, 71, 85, 105));
    }
}
