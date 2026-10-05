using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Playback;
using Xunit;

namespace NovelSpeaker.Infrastructure.IntegrationTests;

public sealed partial class PlaybackCoordinatorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Stopped_projection_is_invalidated_by_source_facts_without_another_audio_stop(bool removal)
    {
        var audio = new FakeLocalAudioPlaybackCoordinator();
        var changes = new SourceChanges();
        await using var coordinator = CreateCoordinator(audio,
            book: CreateBook() with { SourceContext = new("source-1", "catalog-1") }, sourceChanges: changes);
        await coordinator.StartAsync(new("book-1", null, null, null, 10), CancellationToken.None);
        await coordinator.StopAsync(CancellationToken.None);
        changes.Publish(removal
            ? new BookCommittedChange.SourceRemoved("book-1", "source-1")
            : new BookCommittedChange.ActiveCatalogCommitted("book-1", "source-1", "catalog-2"));
        await WaitForAsync(coordinator, () => coordinator.CurrentSnapshot.BookId is null);
        Assert.Equal(1, audio.StopCallCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Active_source_change_invalidates_only_the_current_context(bool clearSource)
    {
        var audio = new FakeLocalAudioPlaybackCoordinator();
        var changes = new SourceChanges();
        await using var coordinator = CreateCoordinator(audio,
            book: CreateBook() with { SourceContext = new("source-1", "catalog-1") }, sourceChanges: changes);
        await coordinator.StartAsync(new("book-1", null, null, null, 10), CancellationToken.None);
        var previous = coordinator.CurrentSnapshot;
        changes.Publish(new BookCommittedChange.ActiveSourceChanged("other-book", "source-1", "source-2"));
        changes.Publish(new BookCommittedChange.ActiveCatalogCommitted("book-1", "other-source", "new-catalog"));
        changes.Publish(new BookCommittedChange.SourceRemoved("book-1", "other-source"));
        Assert.Equal(previous, coordinator.CurrentSnapshot);
        changes.Publish(new BookCommittedChange.ActiveSourceChanged("book-1", "source-1", clearSource ? null : "source-2"));
        await WaitForAsync(coordinator, () => coordinator.CurrentSnapshot.BookId is null);
        await coordinator.StopAsync(CancellationToken.None);
        Assert.Equal(PlaybackState.Idle, coordinator.CurrentSnapshot.State);
        Assert.Equal(1, audio.StopCallCount);
    }

    [Fact]
    public async Task Removal_quiesces_active_source_and_rejects_late_audio_without_stopping_other_source()
    {
        var audio = new FakeLocalAudioPlaybackCoordinator();
        var audioProvider = new FakeAudioGenerationProvider();
        var pending = audioProvider.EnqueuePendingSuccess("old.mp3");
        var progress = new FakeReadingProgressStore();
        var book = CreateBook() with { SourceContext = new("local:book-1", "chapter") };
        await using var coordinator = CreateCoordinator(audio, book: book, audioProvider: audioProvider, readingProgressStore: progress);
        var start = coordinator.StartAsync(new("book-1", null, null, null, 10), CancellationToken.None);
        Assert.Single(audioProvider.Requests);
        var removal = coordinator.StopForRemovalAsync("book-1", "local:book-1", CancellationToken.None);
        pending.CompleteSuccess();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => start);
        await removal.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(PlaybackState.Idle, coordinator.CurrentSnapshot.State);
        Assert.Null(coordinator.CurrentSnapshot.BookId);
        Assert.Null(coordinator.CurrentSnapshot.SourceContext);
        Assert.Equal(0, audio.StartCallCount);
        Assert.Empty(progress.SavedProgress);
        await coordinator.OpenPausedAsync(new("book-1", null, null, null), CancellationToken.None);
        await coordinator.StopForRemovalAsync("book-1", "different-source", CancellationToken.None);
        Assert.Equal(book.SourceContext, coordinator.CurrentSnapshot.SourceContext);
        await coordinator.StopForRemovalAsync("book-1", null, CancellationToken.None);
        Assert.Equal(PlaybackState.Idle, coordinator.CurrentSnapshot.State);
        Assert.Null(coordinator.CurrentSnapshot.BookId);
    }

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
        changes.Publish(new BookCommittedChange.ActiveCatalogCommitted("book-1", "local:book-1", "new-chapter"));
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
        public event EventHandler<BookCommittedChange>? Changed;
        public void Publish(BookCommittedChange change) => Changed?.Invoke(this, change);
    }
}
