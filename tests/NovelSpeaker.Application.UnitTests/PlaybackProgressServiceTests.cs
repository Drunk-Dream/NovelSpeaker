using NovelSpeaker.Application.Playback;
using NovelSpeaker.Domain.Books;
using Xunit;

namespace NovelSpeaker.Application.UnitTests;

public sealed class PlaybackProgressServiceTests
{
    [Fact]
    public async Task SaveAsync_maps_current_segment_character_offset_and_propagates_token()
    {
        var store = new CapturingProgressStore();
        var service = new PlaybackProgressService(store);
        var session = new PlaybackSessionState(CreateBook(), 0, 1, rule: null, speakSpeed: 10);
        var cancellationSource = new CancellationTokenSource();
        session.UpdateAudio(new LocalAudioPlaybackSnapshot(
            PlaybackState.Paused,
            "测试音频",
            "book-1",
            0,
            1,
            321,
            1000,
            null,
            true));

        await service.SaveAsync(session, cancellationSource.Token);

        Assert.Equal(cancellationSource.Token, store.SaveToken);
        Assert.NotNull(store.SavedProgress);
        Assert.Equal(1, store.SavedProgress!.SegmentIndex);
        Assert.Equal(6, store.SavedProgress.CharacterOffset);
        Assert.Equal(321, store.SavedProgress.AudioPositionMilliseconds);
    }

    [Fact]
    public async Task SaveAsync_propagates_store_failure_without_mapping_it_to_success()
    {
        var expected = new InvalidOperationException("保存失败");
        var store = new CapturingProgressStore { SaveFailure = expected };
        var service = new PlaybackProgressService(store);
        var session = new PlaybackSessionState(CreateBook(), 0, 0, rule: null, speakSpeed: 10);

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SaveAsync(session, CancellationToken.None));

        Assert.Same(expected, actual);
    }

    [Fact]
    public async Task RestoreAsync_propagates_cancellation_to_progress_store()
    {
        var store = new CapturingProgressStore();
        var service = new PlaybackProgressService(store);
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            service.RestoreAsync("book-1", cancellationSource.Token));
        Assert.Equal(cancellationSource.Token, store.RestoreToken);
    }

    [Fact]
    public async Task Session_state_owns_audio_protection_and_cancels_old_session_resources()
    {
        var session = new PlaybackSessionState(CreateBook(), 0, 0, rule: null, speakSpeed: 10);
        var cancellationToken = session.CancellationToken;
        var protection = new TrackingDisposable();
        session.ReplaceAudioProtection(protection);

        await session.DisposeAsync();

        Assert.True(cancellationToken.IsCancellationRequested);
        Assert.True(protection.IsDisposed);
    }

    private static PlaybackBookContent CreateBook() =>
        new(
            "book-1",
            "测试小说",
            [
                PlaybackChapterContent.FromLoaded(
                    0,
                    "第一章",
                    [
                        new SpeechSegment(0, 3, 3, "甲", "甲"),
                        new SpeechSegment(1, 6, 6, "乙", "乙")
                    ])
            ]);

    private sealed class CapturingProgressStore : IReadingProgressStore
    {
        public PlaybackProgressUpdate? SavedProgress { get; private set; }

        public CancellationToken SaveToken { get; private set; }

        public CancellationToken RestoreToken { get; private set; }

        public Exception? SaveFailure { get; init; }

        public Task SaveAsync(PlaybackProgressUpdate progress, CancellationToken cancellationToken)
        {
            SaveToken = cancellationToken;
            if (SaveFailure is not null)
            {
                return Task.FromException(SaveFailure);
            }

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

    private sealed class TrackingDisposable : IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose() => IsDisposed = true;
    }
}
