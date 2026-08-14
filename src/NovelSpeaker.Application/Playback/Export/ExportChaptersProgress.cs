namespace NovelSpeaker.Application.Playback.Export;

public sealed record ExportChaptersProgress(
    int CompletedChapterCount,
    int TotalChapterCount,
    int CurrentChapterIndex);
