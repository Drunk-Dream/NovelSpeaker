using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Books.Library;
using Xunit;

namespace NovelSpeaker.Application.UnitTests.Books;

public sealed class BookMetadataUpdateServiceTests
{
    [Fact]
    public async Task UpdateMetadataAsync_normalizes_and_publishes_only_after_commit_despite_observer_failure()
    {
        var store = new DeferredMetadataStore();
        var changes = new BookSourceChanges();
        var committed = new List<BookCommittedChange>();
        changes.Changed += (_, _) => throw new InvalidOperationException("observer failed");
        changes.Changed += (_, change) => committed.Add(change);
        var service = new BookMetadataUpdateService(store, new BookMutationGate(), changes);

        var update = service.UpdateMetadataAsync(new("book", "  title  ", "   "), CancellationToken.None);
        await store.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Empty(committed);
        Assert.Equal(new BookMetadataUpdateRequest("book", "title", null), store.Request);
        store.Committed.SetResult(new BookDetailsHeader("book", "title", null));

        var result = await update;

        Assert.Equal("title", result.Title);
        Assert.Equal(new BookCommittedChange.MetadataCommitted("book"), Assert.Single(committed));
    }

    [Fact]
    public async Task UpdateMetadataAsync_does_not_publish_failed_or_cancelled_persistence()
    {
        foreach (var cancel in new[] { false, true })
        {
            using var cancellation = new CancellationTokenSource();
            var store = new DeferredMetadataStore();
            var changes = new BookSourceChanges();
            var committed = new List<BookCommittedChange>();
            changes.Changed += (_, change) => committed.Add(change);
            var service = new BookMetadataUpdateService(store, new BookMutationGate(), changes);
            var update = service.UpdateMetadataAsync(new("book", "title", "author"), cancellation.Token);
            await store.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (cancel)
            {
                cancellation.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => update);
            }
            else
            {
                store.Committed.SetException(new InvalidOperationException("commit failed"));
                await Assert.ThrowsAsync<InvalidOperationException>(() => update);
            }
            Assert.Empty(committed);
        }
    }

    [Fact]
    public async Task UpdateMetadataAsync_rejects_blank_title_before_persistence()
    {
        var store = new DeferredMetadataStore();
        var service = new BookMetadataUpdateService(store, new BookMutationGate(), new BookSourceChanges());

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateMetadataAsync(new("book", "   ", null), CancellationToken.None));
        Assert.Null(store.Request);
    }

    private sealed class DeferredMetadataStore : IBookMetadataStore
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<BookDetailsHeader> Committed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public BookMetadataUpdateRequest? Request { get; private set; }

        public Task<BookDetailsHeader> UpdateAsync(BookMetadataUpdateRequest request, CancellationToken cancellationToken)
        {
            Request = request;
            Entered.SetResult();
            return Committed.Task.WaitAsync(cancellationToken);
        }
    }
}
