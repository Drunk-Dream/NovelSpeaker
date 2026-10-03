using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Playback;
using Xunit;

namespace NovelSpeaker.Infrastructure.IntegrationTests;

public sealed partial class PlaybackCoordinatorTests
{
    [Fact]
    public async Task Restart_clamps_shrunken_catalog_and_chapter_before_checkpoint()
    {
        var audio = new FakeLocalAudioPlaybackCoordinator();
        var progress = new FakeReadingProgressStore
        {
            StoredProgress = new ReadingProgressEntry("book-1", 12, 99, 999, 777, DateTimeOffset.UnixEpoch)
        };
        await using var coordinator = CreateCoordinator(audio, book: CreateTwoChapterBook(), readingProgressStore: progress);
        await coordinator.OpenPausedAsync(new OpenBookPlaybackRequest("book-1", null, null, null), CancellationToken.None);
        Assert.Equal(1, coordinator.CurrentSnapshot.ChapterIndex);
        Assert.Equal(0, coordinator.CurrentSnapshot.SegmentIndex);
        var checkpoint = Assert.Single(progress.SavedProgress);
        Assert.Equal(1, checkpoint.ChapterIndex);
        Assert.Equal(0, checkpoint.SegmentIndex);
        Assert.Equal(6, checkpoint.CharacterOffset);
        Assert.Equal(0, checkpoint.AudioPositionMilliseconds);
        Assert.Equal(0, audio.StartCallCount);
    }

    [Fact]
    public async Task Empty_active_catalog_does_not_start_or_checkpoint()
    {
        var audio = new FakeLocalAudioPlaybackCoordinator();
        var progress = new FakeReadingProgressStore();
        await using var coordinator = CreateCoordinator(audio, book: new PlaybackBookContent("book-1", "保留显示名称", []),
            readingProgressStore: progress);
        await coordinator.StartAsync(new PlaybackStartRequest("book-1", null, null, null, 10), CancellationToken.None);
        Assert.Equal(PlaybackState.Faulted, coordinator.CurrentSnapshot.State);
        Assert.Equal(0, audio.StartCallCount);
        Assert.Empty(progress.SavedProgress);
    }

    [Fact]
    public async Task Committed_catalog_change_rejects_late_audio_and_new_session_uses_new_source()
    {
        var audio = new FakeLocalAudioPlaybackCoordinator();
        var audioProvider = new FakeAudioGenerationProvider();
        var pending = audioProvider.EnqueuePendingSuccess("old.mp3");
        var content = new FakeBookPlaybackContentService(CreateBook() with { SourceContext = new("local:book-1", "old-chapter") });
        var changes = new SourceChanges();
        var progress = new FakeReadingProgressStore();
        await using var coordinator = CreateCoordinator(audio, bookContentService: content, audioProvider: audioProvider,
            readingProgressStore: progress, sourceChanges: changes);
        var discarded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.SnapshotChanged += (_, snapshot) =>
        {
            if (snapshot.State == PlaybackState.Idle && snapshot.BookId is null) discarded.TrySetResult();
        };
        var start = coordinator.StartAsync(new PlaybackStartRequest("book-1", null, null, null, 10), CancellationToken.None);
        Assert.Single(audioProvider.Requests);
        content.Book = CreateTwoChapterBook() with { SourceContext = new("local:book-1", "new-chapter") };
        changes.Publish(new("book-1", "local:book-1", "new-chapter"));
        pending.CompleteSuccess();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => start);
        await discarded.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, audio.StartCallCount);
        Assert.Empty(progress.SavedProgress);
        await coordinator.StartAsync(new PlaybackStartRequest("book-1", 1, 0, null, 10), CancellationToken.None);
        Assert.Equal(content.Book.SourceContext, coordinator.CurrentSnapshot.SourceContext);
        Assert.Equal(1, coordinator.CurrentSnapshot.ChapterIndex);
        Assert.Equal(1, audio.StartCallCount);
        Assert.DoesNotContain(progress.SavedProgress, p => p.SourceContext?.CatalogVersion == "old-chapter");
    }

    private sealed class SourceChanges : IBookSourceChangeSource
    {
        public event EventHandler<BookSourceCatalogChanged>? CatalogChanged;
        public void Publish(BookSourceCatalogChanged change) => CatalogChanged?.Invoke(this, change);
    }
}
