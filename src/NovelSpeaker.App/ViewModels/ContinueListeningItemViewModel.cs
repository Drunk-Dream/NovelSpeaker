using NovelSpeaker.App.Library;

namespace NovelSpeaker.App.ViewModels;

public sealed class ContinueListeningItemViewModel
{
    public ContinueListeningItemViewModel(
        string bookId,
        string title,
        string currentChapterTitle,
        string remainingChapterText,
        double progressRatio,
        GeneratedBookCover cover)
    {
        BookId = bookId;
        Title = title;
        CurrentChapterTitle = currentChapterTitle;
        RemainingChapterText = remainingChapterText;
        ProgressRatio = Math.Clamp(progressRatio, 0, 1);
        Cover = cover;
    }

    public string BookId { get; }

    public string Title { get; }

    public string CurrentChapterTitle { get; }

    public string RemainingChapterText { get; }

    public double ProgressRatio { get; }

    public GeneratedBookCover Cover { get; }
}
