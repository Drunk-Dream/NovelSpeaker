namespace NovelSpeaker.Application.Playback.Export;

public enum ChapterExportBatchStatus
{
    Waiting,
    Running,
    Cancelling,
    Completed,
    Cancelled,
    Failed
}
