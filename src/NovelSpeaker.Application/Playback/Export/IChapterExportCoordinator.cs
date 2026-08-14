namespace NovelSpeaker.Application.Playback.Export;

/// <summary>
/// Owns the single process-wide chapter MP3 export batch independently of any page.
/// </summary>
public interface IChapterExportCoordinator
{
    ChapterExportSnapshot? CurrentSnapshot { get; }

    event EventHandler<ChapterExportSnapshot>? SnapshotChanged;

    Task<ChapterExportStartResult> StartAsync(
        StartChapterExportRequest request,
        CancellationToken cancellationToken);

    Task CancelAsync(CancellationToken cancellationToken);

    Task WaitForCurrentBatchAsync(CancellationToken cancellationToken);
}
