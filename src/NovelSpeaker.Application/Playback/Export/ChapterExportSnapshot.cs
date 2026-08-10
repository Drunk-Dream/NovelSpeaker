namespace NovelSpeaker.Application.Playback.Export;

public sealed record ChapterExportSnapshot(
    Guid BatchId,
    string BookId,
    string BookTitle,
    ChapterExportBatchStatus Status,
    int TotalChapterCount,
    int CompletedChapterCount,
    int SkippedChapterCount,
    int? CurrentChapterIndex,
    string? CurrentChapterTitle,
    string DestinationRootDirectory,
    string? ExportDirectoryPath,
    string? ErrorSummary)
{
    public double Progress => TotalChapterCount <= 0
        ? 0d
        : Math.Clamp((double)CompletedChapterCount / TotalChapterCount, 0d, 1d);
}
