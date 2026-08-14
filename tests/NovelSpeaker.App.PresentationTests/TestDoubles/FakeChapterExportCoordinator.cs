using NovelSpeaker.Application.Playback.Export;

namespace NovelSpeaker.App.PresentationTests.TestDoubles;

internal sealed class FakeChapterExportCoordinator : IChapterExportCoordinator
{
    public FakeChapterExportCoordinator(ChapterExportSnapshot? snapshot = null)
    {
        CurrentSnapshot = snapshot;
    }

    public ChapterExportSnapshot? CurrentSnapshot { get; private set; }

    public ChapterExportStartResult StartResult { get; set; } =
        new(ChapterExportStartStatus.Accepted, Guid.NewGuid(), null);

    public int StartCallCount { get; private set; }

    public int CancelCallCount { get; private set; }

    public StartChapterExportRequest? LastRequest { get; private set; }

    public event EventHandler<ChapterExportSnapshot>? SnapshotChanged;

    public Task<ChapterExportStartResult> StartAsync(
        StartChapterExportRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StartCallCount++;
        LastRequest = request;
        return Task.FromResult(StartResult);
    }

    public Task CancelAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CancelCallCount++;
        return Task.CompletedTask;
    }

    public Task WaitForCurrentBatchAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public void Publish(ChapterExportSnapshot snapshot)
    {
        CurrentSnapshot = snapshot;
        SnapshotChanged?.Invoke(this, snapshot);
    }
}
