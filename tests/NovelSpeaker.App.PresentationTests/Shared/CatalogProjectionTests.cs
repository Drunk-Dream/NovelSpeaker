using System.Collections.Specialized;
using System.ComponentModel;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Domain.Books;
using NovelSpeaker.App.Shared.Presentation.Platform;
using NovelSpeaker.TestKit.Navigation;
using Xunit;

namespace NovelSpeaker.App.PresentationTests.Shared;

public sealed class CatalogProjectionTests
{
    [Fact]
    public void Replacing_10000_items_emits_one_reset_instead_of_item_adds()
    {
        var collection = new ResettableObservableCollection<int>();
        var changes = new List<NotifyCollectionChangedAction>();
        collection.CollectionChanged += (_, args) => changes.Add(args.Action);

        collection.ReplaceWith(Enumerable.Range(0, 10_000), static value => value);

        Assert.Equal(10_000, collection.Count);
        Assert.Equal([NotifyCollectionChangedAction.Reset], changes);
        Assert.Equal(9_999, collection[9_999]);
    }

    [Fact]
    public void Indexed_catalog_resolves_key_and_position_without_scanning_the_catalog()
    {
        var items = Enumerable.Range(0, 10_000)
            .Select(static id => new CatalogItem(id))
            .ToArray();
        var catalog = new IndexedCatalog<CatalogItem>(items, static item => item.Id);

        Assert.Equal(10_000, catalog.Count);
        Assert.True(catalog.TryGet(9_999, out var item));
        Assert.Equal(9_999, item.Id);
        Assert.True(catalog.TryGetPosition(9_999, out var position));
        Assert.Equal(9_999, position);
        Assert.False(catalog.TryGet(10_000, out _));
    }

    [Fact]
    public void Sparse_decoration_retains_only_changed_keys()
    {
        var decoration = new SparseCatalogDecoration<string>();

        decoration.Set(10, "current");
        decoration.Set(9_999, "selected");
        decoration.Set(10, "updated");

        Assert.Equal(2, decoration.Count);
        Assert.True(decoration.TryGet(10, out var current));
        Assert.Equal("updated", current);
        Assert.True(decoration.Remove(10));
        Assert.Equal(1, decoration.Count);
    }

    [Fact]
    public async Task Staged_replacement_preserves_updates_to_rows_already_appended()
    {
        var collection = new ResettableObservableCollection<int>();
        var scheduler = new MutatingUiScheduler(
            beforeInvocation: invocation =>
            {
                if (invocation == 3)
                {
                    collection.ReplaceAt(0, 42);
                }
            });

        await collection.ReplaceWithInBatchesAsync(
            Enumerable.Range(0, 512).ToArray(),
            static value => value,
            scheduler,
            CancellationToken.None,
            batchSize: 256);

        Assert.Equal(42, collection[0]);
        Assert.Equal(511, collection[511]);
    }

    [Fact]
    public async Task Staged_replacement_aborts_cleanly_when_cancelled_between_batches()
    {
        var collection = new ResettableObservableCollection<int>();
        using var cancellation = new CancellationTokenSource();
        var scheduler = new MutatingUiScheduler(
            beforeInvocation: invocation =>
            {
                if (invocation == 2)
                {
                    cancellation.Cancel();
                }
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            collection.ReplaceWithInBatchesAsync(
                Enumerable.Range(0, 512).ToArray(),
                static value => value,
                scheduler,
                cancellation.Token,
                batchSize: 256));

        Assert.Empty(collection);
    }

    [Fact]
    public async Task Staged_replacement_is_invalidated_by_a_lifecycle_clear_between_batches()
    {
        var collection = new ResettableObservableCollection<int>();
        var scheduler = new MutatingUiScheduler(
            beforeInvocation: invocation =>
            {
                if (invocation == 3)
                {
                    collection.Clear();
                }
            });

        await collection.ReplaceWithInBatchesAsync(
            Enumerable.Range(0, 512).ToArray(),
            static value => value,
            scheduler,
            CancellationToken.None,
            batchSize: 256);

        Assert.Empty(collection);
    }

    [Fact]
    public async Task Staged_replacement_is_invalidated_by_a_synchronous_replacement_between_batches()
    {
        var collection = new ResettableObservableCollection<int>();
        var scheduler = new MutatingUiScheduler(
            beforeInvocation: invocation =>
            {
                if (invocation == 3)
                {
                    collection.ReplaceWith([9000]);
                }
            });

        await collection.ReplaceWithInBatchesAsync(
            Enumerable.Range(0, 512).ToArray(),
            static value => value,
            scheduler,
            CancellationToken.None,
            batchSize: 256);

        Assert.Equal([9000], collection);
    }

    [Fact]
    public async Task Staged_replacement_waiting_for_the_gate_is_invalidated_by_a_synchronous_replacement()
    {
        var collection = new ResettableObservableCollection<int>();
        var scheduler = new GateUiScheduler();
        var first = collection.ReplaceWithInBatchesAsync(
            Enumerable.Range(0, 512).ToArray(),
            static value => value,
            scheduler,
            CancellationToken.None,
            batchSize: 256);
        await scheduler.FirstInvocation.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var second = collection.ReplaceWithInBatchesAsync(
            Enumerable.Range(1000, 512).ToArray(),
            static value => value,
            scheduler,
            CancellationToken.None,
            batchSize: 256);
        collection.ReplaceWith([9001]);
        scheduler.ReleaseFirstInvocation();

        await first;
        await second;

        Assert.Equal([9001], collection);
    }

    [Fact]
    public async Task Applying_an_unchanged_cache_status_preserves_the_existing_current_item()
    {
        var book = new PlaybackBookContent(
            "book-1",
            "测试书",
            [PlaybackChapterContent.Unloaded(0, "第一章")]);
        var service = new StubPlaybackContentService(book);
        var projection = new PlayerContentController(new PlaybackBackedBookDetailsQuery(service), service, new InlineUiScheduler());

        await projection.EnsureBookLoadedAsync("book-1", 0, 0, CancellationToken.None);
        var currentItem = projection.CurrentChapterItem;

        projection.ApplyChapterCacheStatus(0, 0, totalSegmentCount: null);

        Assert.Same(currentItem, projection.Chapters[0]);
        Assert.Same(currentItem, projection.CurrentChapterItem);
    }

    [Fact]
    public async Task Moving_current_chapter_updates_only_the_old_and_new_items()
    {
        var book = new PlaybackBookContent(
            "book-1",
            "测试书",
            [
                PlaybackChapterContent.Unloaded(0, "第一章"),
                PlaybackChapterContent.Unloaded(1, "第二章")
            ]);
        var service = new StubPlaybackContentService(book);
        var projection = new PlayerContentController(new PlaybackBackedBookDetailsQuery(service), service, new InlineUiScheduler());

        await projection.EnsureBookLoadedAsync("book-1", 0, 0, CancellationToken.None);

        var previousItems = projection.Chapters.ToArray();

        projection.ApplyPosition(1, 0, 0);

        Assert.NotSame(previousItems[0], projection.Chapters[0]);
        Assert.NotSame(previousItems[1], projection.Chapters[1]);
        Assert.False(projection.Chapters[0].IsCurrent);
        Assert.True(projection.Chapters[1].IsCurrent);
        Assert.Same(projection.Chapters[1], projection.CurrentChapterItem);
    }

    [Fact]
    public async Task Player_projection_loads_a_10000_chapter_catalog_with_bounded_reset_notifications()
    {
        var book = new PlaybackBookContent(
            "book-1",
            "测试书",
            Enumerable.Range(0, 10_000)
                .Select(static index => PlaybackChapterContent.Unloaded(index, $"第 {index + 1} 章"))
                .ToArray());
        var service = new StubPlaybackContentService(book);
        var projection = new PlayerContentController(new PlaybackBackedBookDetailsQuery(service), service, new InlineUiScheduler());
        var changes = new List<NotifyCollectionChangedAction>();
        projection.Chapters.CollectionChanged += (_, args) => changes.Add(args.Action);

        await projection.EnsureBookLoadedAsync("book-1", 9_999, 0, CancellationToken.None);

        Assert.Equal(10_000, projection.Chapters.Count);
        Assert.Equal(41, changes.Count);
        Assert.All(changes, static action => Assert.Equal(NotifyCollectionChangedAction.Reset, action));
        Assert.True(projection.TryGetChapterItem(9_999, out var currentItem));
        Assert.Same(currentItem, projection.CurrentChapterItem);
    }

    [Fact]
    public async Task Player_catalog_locates_beginning_middle_and_tail_by_index()
    {
        var book = new PlaybackBookContent(
            "book-1",
            "测试书",
            Enumerable.Range(0, 10_000)
                .Select(static index => PlaybackChapterContent.Unloaded(index, $"第 {index + 1} 章"))
                .ToArray());
        var service = new StubPlaybackContentService(book);
        var projection = new PlayerContentController(new PlaybackBackedBookDetailsQuery(service), service, new InlineUiScheduler());

        await projection.EnsureBookLoadedAsync("book-1", 5_000, 0, CancellationToken.None);

        Assert.Equal(0, projection.GetChapterPosition(0));
        Assert.Equal(5_000, projection.GetChapterPosition(5_000));
        Assert.Equal(9_999, projection.GetChapterPosition(9_999));
        Assert.Null(projection.GetChapterPosition(10_000));
    }

    [Fact]
    public async Task Player_selection_decoration_for_10000_chapters_emits_one_reset()
    {
        var book = new PlaybackBookContent(
            "book-1",
            "测试书",
            Enumerable.Range(0, 10_000)
                .Select(static index => PlaybackChapterContent.Unloaded(index, $"第 {index + 1} 章"))
                .ToArray());
        var service = new StubPlaybackContentService(book);
        var projection = new PlayerContentController(new PlaybackBackedBookDetailsQuery(service), service, new InlineUiScheduler());
        await projection.EnsureBookLoadedAsync("book-1", 0, 0, CancellationToken.None);

        var changes = new List<NotifyCollectionChangedAction>();
        projection.Chapters.CollectionChanged += (_, args) => changes.Add(args.Action);
        projection.ApplyChapterSelections(
            Enumerable.Range(0, 10_000)
                .Select(static index => (ChapterIndex: index, IsSelected: true))
                .ToArray(),
            notify: false);
        projection.NotifyChapterReset();

        Assert.Equal([NotifyCollectionChangedAction.Reset], changes);
        Assert.All(projection.Chapters, static chapter => Assert.True(chapter.IsSelectedForActiveCache));
    }

    [Fact]
    public async Task Invalidating_pending_book_and_chapter_loads_discards_late_results()
    {
        var book = new PlaybackBookContent(
            "book-1",
            "测试书",
            [PlaybackChapterContent.Unloaded(0, "第一章")]);
        var service = new DelayedPlaybackContentService();
        var projection = new PlayerContentController(new PlaybackBackedBookDetailsQuery(service), service, new InlineUiScheduler());

        var bookLoad = projection.EnsureBookLoadedAsync("book-1", 0, 0, CancellationToken.None);
        await service.BookRequested.Task;
        projection.InvalidatePendingLoads();
        service.BookResult.TrySetResult(book);

        Assert.Null(await bookLoad);
        Assert.Empty(projection.Chapters);

        await projection.EnsureBookLoadedAsync("book-1", 0, 0, CancellationToken.None);
        var snapshot = PlaybackSnapshot.Idle with
        {
            State = PlaybackState.Paused,
            BookId = "book-1",
            ChapterIndex = 0,
            SegmentCount = 1,
            ContentRevision = 1
        };
        var chapterLoad = projection.EnsureContentLoadedAsync(snapshot, CancellationToken.None);
        await service.ChapterRequested.Task;
        projection.InvalidatePendingLoads();
        service.ChapterResult.TrySetResult(
            PlaybackChapterContent.FromLoaded(
                0,
                "第一章",
                [new SpeechSegment(0, 0, 4, "正文", "正文")]));

        await chapterLoad;

        Assert.False(projection.IsChapterLoaded(0));
        Assert.Empty(projection.Segments);
    }

    private sealed record CatalogItem(int Id);

    private sealed class StubPlaybackContentService(PlaybackBookContent book) : IBookPlaybackContentService
    {
        public Task<PlaybackBookContent?> GetBookAsync(
            string bookId,
            CancellationToken cancellationToken) =>
            Task.FromResult<PlaybackBookContent?>(
                string.Equals(book.BookId, bookId, StringComparison.Ordinal) ? book : null);

        public Task<PlaybackChapterContent?> GetChapterAsync(
            string bookId,
            int chapterIndex,
            CancellationToken cancellationToken) =>
            Task.FromResult<PlaybackChapterContent?>(null);
    }

    private sealed class DelayedPlaybackContentService : IBookPlaybackContentService
    {
        public TaskCompletionSource<PlaybackBookContent?> BookResult { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<PlaybackChapterContent?> ChapterResult { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource BookRequested { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ChapterRequested { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<PlaybackBookContent?> GetBookAsync(
            string bookId,
            CancellationToken cancellationToken)
        {
            BookRequested.TrySetResult();
            return BookResult.Task;
        }

        public Task<PlaybackChapterContent?> GetChapterAsync(
            string bookId,
            int chapterIndex,
            CancellationToken cancellationToken)
        {
            ChapterRequested.TrySetResult();
            return ChapterResult.Task;
        }
    }

    private sealed class MutatingUiScheduler(Action<int> beforeInvocation) : IUiScheduler
    {
        private int _invocation;

        public bool CheckAccess() => true;

        public Task InvokeAsync(Action action, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            action();
            return Task.CompletedTask;
        }

        public Task InvokeAsync(Func<Task> action, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return action();
        }

        public Task InvokeLaterAsync(Action action, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            beforeInvocation(++_invocation);
            action();
            return Task.CompletedTask;
        }
    }

    private sealed class GateUiScheduler : IUiScheduler
    {
        private int _invocation;
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource FirstInvocation { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool CheckAccess() => true;

        public Task InvokeAsync(Action action, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            action();
            return Task.CompletedTask;
        }

        public Task InvokeAsync(Func<Task> action, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return action();
        }

        public async Task InvokeLaterAsync(Action action, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Interlocked.Increment(ref _invocation) == 1)
            {
                FirstInvocation.TrySetResult();
                await _release.Task.WaitAsync(cancellationToken);
            }

            action();
        }

        public void ReleaseFirstInvocation() => _release.TrySetResult();
    }

    private sealed class InlineUiScheduler : IUiScheduler
    {
        public bool CheckAccess() => true;

        public Task InvokeAsync(Action action, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            action();
            return Task.CompletedTask;
        }

        public Task InvokeAsync(Func<Task> action, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return action();
        }

        public Task InvokeLaterAsync(Action action, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            action();
            return Task.CompletedTask;
        }
    }
}
