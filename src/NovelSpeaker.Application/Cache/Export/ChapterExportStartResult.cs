namespace NovelSpeaker.Application.Cache.Export;

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
