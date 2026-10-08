using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Books.Library;
using Xunit;

namespace NovelSpeaker.Application.UnitTests.Books;

public sealed class BookDeletionServiceTests
{
    [Fact]
    public async Task DeleteAsync_coordinates_begin_commit_and_completion()
    {
        var store = new RecordingDeletionOperationStore();
        var changes = new BookSourceChanges();
        var committed = new List<BookCommittedChange>();
        var notifiedAfterCommit = false;
        changes.Changed += (_, _) => throw new InvalidOperationException("observer failed");
        changes.Changed += (_, change) =>
        {
            notifiedAfterCommit = store.Calls.SequenceEqual(["stop", "begin", "commit"]);
            committed.Add(change);
        };
        var service = new BookDeletionService(store, new BookMutationGate(), changes, [new RecordingStopper(store)], []);

        var result = await service.DeleteAsync(new BookDeleteRequest("book-1", true), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(["stop", "begin", "commit", "complete"], store.Calls);
        Assert.True(notifiedAfterCommit);
        Assert.Equal(new BookCommittedChange.BookRemoved("book-1"), Assert.Single(committed));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task RemoveAsync_distinguishes_source_removal_active_clear_and_last_source_book_removal(bool lastSource, bool active)
    {
        var store = new RecordingDeletionOperationStore { DeletesBook = lastSource, ActiveSourceId = active ? "source-1" : null };
        var changes = new BookSourceChanges();
        var committed = new List<BookCommittedChange>();
        changes.Changed += (_, _) => throw new InvalidOperationException("observer failed");
        changes.Changed += (_, change) => committed.Add(change);
        var service = new BookDeletionService(store, new BookMutationGate(), changes, [], []);

        var result = await service.RemoveAsync(new("book-1", "source-1"), CancellationToken.None);

        Assert.Equal(lastSource, result!.DeletedBook);
        var expected = new List<BookCommittedChange> { new BookCommittedChange.SourceRemoved("book-1", "source-1") };
        if (lastSource) expected.Add(new BookCommittedChange.BookRemoved("book-1"));
        else if (active) expected.Add(new BookCommittedChange.ActiveSourceChanged("book-1", "source-1", null));
        Assert.Equal(expected, committed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Removal_commit_failure_rolls_back_without_publishing(bool removeSource)
    {
        var store = new RecordingDeletionOperationStore { CommitException = new IOException("commit failed") };
        var changes = new BookSourceChanges();
        var committed = new List<BookCommittedChange>();
        changes.Changed += (_, change) => committed.Add(change);
        var service = new BookDeletionService(store, new BookMutationGate(), changes, [], []);

        await Assert.ThrowsAsync<IOException>(async () =>
        {
            if (removeSource) await service.RemoveAsync(new("book-1", "source-1"), CancellationToken.None);
            else await service.DeleteAsync(new("book-1", true), CancellationToken.None);
        });

        Assert.Equal(["begin", "commit", "rollback"], store.Calls);
        Assert.Empty(committed);
    }

    [Fact]
    public async Task DeleteAsync_keeps_committed_fact_when_file_completion_needs_recovery()
    {
        var store = new RecordingDeletionOperationStore { CompleteException = new IOException("failed") };
        var changes = new BookSourceChanges();
        var committed = new List<BookCommittedChange>();
        changes.Changed += (_, change) => committed.Add(change);
        var service = new BookDeletionService(store, new BookMutationGate(), changes, [], []);

        await Assert.ThrowsAsync<IOException>(() =>
            service.DeleteAsync(new BookDeleteRequest("book-1", true), CancellationToken.None));

        Assert.Equal(["begin", "commit", "complete", "rollback"], store.Calls);
        Assert.False(store.RollbackToken.CanBeCanceled);
        Assert.Equal(new BookCommittedChange.BookRemoved("book-1"), Assert.Single(committed));
    }

    [Fact]
    public async Task DeleteAsync_returns_null_without_committing_when_book_is_missing()
    {
        var store = new RecordingDeletionOperationStore { IsMissing = true };
        var changes = new BookSourceChanges();
        var committed = new List<BookCommittedChange>();
        changes.Changed += (_, change) => committed.Add(change);
        var service = new BookDeletionService(store, new BookMutationGate(), changes, [], []);

        Assert.Null(await service.DeleteAsync(new BookDeleteRequest("missing", false), CancellationToken.None));
        Assert.Equal(["begin"], store.Calls);
        Assert.Empty(committed);
    }

    private sealed class RecordingStopper(RecordingDeletionOperationStore store) : IBookRemovalWorkStopper
    {
        public Task StopForRemovalAsync(string bookId, string? sourceId, CancellationToken cancellationToken)
        {
            store.Calls.Add("stop");
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingDeletionOperationStore : IBookDeletionOperationStore
    {
        public List<string> Calls { get; } = [];
        public bool IsMissing { get; init; }
        public Exception? CompleteException { get; init; }
        public Exception? CommitException { get; init; }
        public bool DeletesBook { get; init; } = true;
        public string? ActiveSourceId { get; init; }
        public CancellationToken RollbackToken { get; private set; }

        public Task<BookDeletionPreparation?> BeginAsync(BookDeleteRequest request, CancellationToken cancellationToken)
        {
            Calls.Add("begin");
            return Task.FromResult(IsMissing
                ? null
                : new BookDeletionPreparation(
                    "operation-1",
                    new BookDeleteResult(request.BookId, request.DeleteAudioCache, 2, true), DeletesBook, ActiveSourceId));
        }

        public Task<BookDeletionPreparation?> BeginSourceRemovalAsync(BookSourceRemoveRequest request, CancellationToken cancellationToken) =>
            BeginAsync(new BookDeleteRequest(request.BookId, true), cancellationToken);

        public Task CommitAsync(BookDeletionPreparation preparation, CancellationToken cancellationToken)
        {
            Calls.Add("commit");
            return CommitException is null ? Task.CompletedTask : Task.FromException(CommitException);
        }

        public Task CompleteAsync(BookDeletionPreparation preparation, CancellationToken cancellationToken)
        {
            Calls.Add("complete");
            return CompleteException is null ? Task.CompletedTask : Task.FromException(CompleteException);
        }

        public Task RollbackAsync(BookDeletionPreparation preparation, CancellationToken cancellationToken)
        {
            Calls.Add("rollback");
            RollbackToken = cancellationToken;
            return Task.CompletedTask;
        }
    }
}
