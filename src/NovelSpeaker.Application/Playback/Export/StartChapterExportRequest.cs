namespace NovelSpeaker.Application.Playback.Export;

public sealed record StartChapterExportRequest(
    string BookId,
    string BookTitle,
    IReadOnlyCollection<ChapterExportSelection> Chapters,
    string DestinationRootDirectory,
    int SkippedChapterCount = 0);
