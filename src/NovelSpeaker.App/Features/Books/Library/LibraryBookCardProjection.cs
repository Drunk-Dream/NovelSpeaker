using NovelSpeaker.App.Features.Books.Shared;
using NovelSpeaker.Application.Playback;

namespace NovelSpeaker.App.Features.Books.Library;

/// <summary>
/// Immutable, lightweight data projected for one library card. Playback changes replace
/// only the affected projection item; the catalog remains the source of stable fields.
/// </summary>
public sealed class LibraryBookCardProjection
{
    private readonly IBookCoverGenerator? _coverGenerator;
    private readonly GeneratedBookCover? _initialCover;
    private GeneratedBookCover? _cover;

    public LibraryBookCardProjection(
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
        : this(
            bookId,
            title,
            displayAuthor,
            currentChapterTitle,
            remainingChapterText,
            progressRatio,
            hasReadingProgress,
            lastPlayedAt,
            coverGenerator: null,
            cover,
            canDelete)
    {
    }

    internal LibraryBookCardProjection(
        string bookId,
        string title,
        string displayAuthor,
        string currentChapterTitle,
        string remainingChapterText,
        double progressRatio,
        bool hasReadingProgress,
        string? lastPlayedAt,
        IBookCoverGenerator coverGenerator,
        bool canDelete)
        : this(
            bookId,
            title,
            displayAuthor,
            currentChapterTitle,
            remainingChapterText,
            progressRatio,
            hasReadingProgress,
            lastPlayedAt,
            coverGenerator,
            initialCover: null,
            canDelete)
    {
        ArgumentNullException.ThrowIfNull(coverGenerator);
    }

    private LibraryBookCardProjection(
        string bookId,
        string title,
        string displayAuthor,
        string currentChapterTitle,
        string remainingChapterText,
        double progressRatio,
        bool hasReadingProgress,
        string? lastPlayedAt,
        IBookCoverGenerator? coverGenerator,
        GeneratedBookCover? initialCover,
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
        _coverGenerator = coverGenerator;
        _initialCover = initialCover;
        _cover = initialCover;
        CanDelete = canDelete;
    }

    public string BookId { get; }

    public string Title { get; }

    public string DisplayAuthor { get; }

    public string CurrentChapterTitle { get; }

    public string CurrentChapterToolTip => CurrentChapterTitle;

    public string RemainingChapterText { get; }

    public double ProgressRatio { get; }

    public bool HasReadingProgress { get; }

    public string? LastPlayedAt { get; }

    public bool CanDelete { get; }

    public GeneratedBookCover Cover => _cover ??= _initialCover ?? _coverGenerator?.Generate(Title)
        ?? throw new InvalidOperationException("A book cover has not been configured.");

    public string TitleToolTip => Title;

    public string ProgressAutomationText => HasReadingProgress
        ? $"总体进度 {Math.Round(ProgressRatio * 100d, 0):0}%"
        : "尚无阅读进度";

    public string AutomationName =>
        $"打开《{Title}》，作者 {DisplayAuthor}，当前章节 {CurrentChapterTitle}，{RemainingChapterText}，{ProgressAutomationText}";

    public string MoreActionsAutomationName => $"《{Title}》的更多操作";

    public LibraryBookCardProjection WithEffectiveProgress(
        EffectiveReadingProgress progress,
        string remainingChapterText)
    {
        ArgumentNullException.ThrowIfNull(progress);
        return new LibraryBookCardProjection(
            BookId,
            Title,
            DisplayAuthor,
            progress.CurrentChapterTitle,
            remainingChapterText,
            progress.OverallProgress,
            progress.HasReadingProgress,
            LastPlayedAt,
            _coverGenerator,
            _cover,
            CanDelete);
    }

    public bool HasSameEffectiveProgress(
        EffectiveReadingProgress progress,
        string remainingChapterText)
    {
        ArgumentNullException.ThrowIfNull(progress);
        return string.Equals(CurrentChapterTitle, progress.CurrentChapterTitle, StringComparison.Ordinal) &&
            string.Equals(RemainingChapterText, remainingChapterText, StringComparison.Ordinal) &&
            ProgressRatio == Math.Clamp(progress.OverallProgress, 0, 1) &&
            HasReadingProgress == progress.HasReadingProgress;
    }

    internal bool CanReuseFor(
        string title,
        string displayAuthor,
        string? lastPlayedAt)
    {
        return string.Equals(Title, title, StringComparison.Ordinal) &&
            string.Equals(DisplayAuthor, displayAuthor, StringComparison.Ordinal) &&
            string.Equals(LastPlayedAt, lastPlayedAt, StringComparison.Ordinal);
    }
}
