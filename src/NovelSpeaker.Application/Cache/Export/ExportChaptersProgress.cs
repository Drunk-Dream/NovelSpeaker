namespace NovelSpeaker.Application.Cache.Export;

public sealed record ExportChaptersProgress(
    int CompletedChapterCount,
    int TotalChapterCount,
    int CurrentChapterIndex);
