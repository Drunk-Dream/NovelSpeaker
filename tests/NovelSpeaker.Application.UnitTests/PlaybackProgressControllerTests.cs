using NovelSpeaker.Application.Playback;
using Xunit;

namespace NovelSpeaker.Application.UnitTests;

public sealed class PlaybackProgressControllerTests
{
    [Fact]
    public async Task SaveAsync_persists_committed_checkpoint_and_propagates_token()
    {
        var store = new CapturingProgressStore();
        var service = new PlaybackProgressController(store);
        var progress = new PlaybackProgressUpdate("book-1", 4, 1, 6, 321);
        using var cancellationSource = new CancellationTokenSource();

        await service.SaveAsync(progress, cancellationSource.Token);

        Assert.Equal(cancellationSource.Token, store.SaveToken);
        Assert.Same(progress, store.SavedProgress);
    }

    [Fact]
    public async Task SaveAsync_propagates_store_failure_without_mapping_it_to_success()
    {
        var expected = new InvalidOperationException("保存失败");
        var store = new CapturingProgressStore { SaveFailure = expected };
        var service = new PlaybackProgressController(store);

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SaveAsync(new PlaybackProgressUpdate("book-1", 0, 0, 0, 0), CancellationToken.None));

        Assert.Same(expected, actual);
    }

    [Fact]
    public async Task SaveAsync_cancelled_checkpoint_does_not_reach_store()
    {
        var store = new CapturingProgressStore();
        var service = new PlaybackProgressController(store);
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            service.SaveAsync(new PlaybackProgressUpdate("book-1", 0, 0, 0, 0), cancellationSource.Token));

        Assert.Null(store.SavedProgress);
    }

    [Fact]
    public async Task RestoreAsync_propagates_cancellation_to_progress_store()
    {
        var store = new CapturingProgressStore();
        var service = new PlaybackProgressController(store);
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            service.RestoreAsync("book-1", cancellationSource.Token));
        Assert.Equal(cancellationSource.Token, store.RestoreToken);
    }

    private sealed class CapturingProgressStore : IReadingProgressStore
    {
        public PlaybackProgressUpdate? SavedProgress { get; private set; }
        public CancellationToken SaveToken { get; private set; }
        public CancellationToken RestoreToken { get; private set; }
        public Exception? SaveFailure { get; init; }

        public Task SaveAsync(PlaybackProgressUpdate progress, CancellationToken cancellationToken)
        {
            SaveToken = cancellationToken;
            if (SaveFailure is not null) return Task.FromException(SaveFailure);
            SavedProgress = progress;
            return Task.CompletedTask;
        }

        public Task<ReadingProgressEntry?> GetAsync(string bookId, CancellationToken cancellationToken)
        {
            RestoreToken = cancellationToken;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<ReadingProgressEntry?>(null);
        }

        public Task<ReadingProgressEntry?> GetMostRecentAsync(CancellationToken cancellationToken) =>
            Task.FromResult<ReadingProgressEntry?>(null);
    }
}
