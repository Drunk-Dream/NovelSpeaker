namespace NovelSpeaker.Application.Cache.Export;

public enum ChapterExportBatchStatus
{
    Waiting,
    Running,
    Cancelling,
    Completed,
    Cancelled,
    Failed
}
