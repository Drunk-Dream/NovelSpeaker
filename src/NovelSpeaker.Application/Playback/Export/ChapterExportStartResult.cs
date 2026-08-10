namespace NovelSpeaker.Application.Playback.Export;

public enum ChapterExportStartStatus
{
    Accepted,
    BatchAlreadyActive,
    NoChaptersSelected
}

public sealed record ChapterExportStartResult(
    ChapterExportStartStatus Status,
    Guid? BatchId,
    string? Message);
