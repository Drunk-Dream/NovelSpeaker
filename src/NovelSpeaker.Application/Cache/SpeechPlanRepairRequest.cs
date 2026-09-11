namespace NovelSpeaker.Application.Cache;

/// <summary>Identifies a chapter whose persisted speech plan needs rebuilding.</summary>
public sealed record SpeechPlanRepairRequest
{
    public SpeechPlanRepairRequest(string bookId, int chapterIndex, string chapterId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookId);
        ArgumentException.ThrowIfNullOrWhiteSpace(chapterId);
        ArgumentOutOfRangeException.ThrowIfNegative(chapterIndex);
        BookId = bookId;
        ChapterIndex = chapterIndex;
        ChapterId = chapterId;
    }

    public string BookId { get; }

    public int ChapterIndex { get; }

    public string ChapterId { get; }
}
