namespace NovelSpeaker.Application.Playback.Export;

public enum ExportChaptersStatus
{
    Succeeded,
    BookNotFound,
    SelectedRuleUnavailable,
    ChapterNotFound,
    ChapterSpeechPlanUnavailable,
    ChapterHasNoPlayableSegments,
    IncompleteCache
}

public sealed record ExportedChapterMp3(
    int ChapterIndex,
    string FilePath);

public sealed record ExportChaptersResult(
    ExportChaptersStatus Status,
    string? ExportDirectoryPath,
    IReadOnlyList<ExportedChapterMp3> Files,
    int? FailedChapterIndex)
{
    public static ExportChaptersResult Failed(
        ExportChaptersStatus status,
        int? chapterIndex = null) =>
        new(status, null, [], chapterIndex);
}
