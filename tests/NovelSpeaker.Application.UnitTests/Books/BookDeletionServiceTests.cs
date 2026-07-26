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
        var service = new BookDeletionService(store);

        var result = await service.DeleteAsync(new BookDeleteRequest("book-1", true), CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(["begin", "commit", "complete"], store.Calls);
    }

    [Fact]
    public async Task DeleteAsync_rolls_back_when_commit_or_file_completion_fails()
    {
        var store = new RecordingDeletionOperationStore { CompleteException = new IOException("failed") };
        var service = new BookDeletionService(store);

        await Assert.ThrowsAsync<IOException>(() =>
            service.DeleteAsync(new BookDeleteRequest("book-1", true), CancellationToken.None));

        Assert.Equal(["begin", "commit", "complete", "rollback"], store.Calls);
        Assert.False(store.RollbackToken.CanBeCanceled);
    }

    [Fact]
    public async Task DeleteAsync_returns_null_without_committing_when_book_is_missing()
    {
        var store = new RecordingDeletionOperationStore { IsMissing = true };
        var service = new BookDeletionService(store);

        Assert.Null(await service.DeleteAsync(new BookDeleteRequest("missing", false), CancellationToken.None));
        Assert.Equal(["begin"], store.Calls);
    }

    private sealed class RecordingDeletionOperationStore : IBookDeletionOperationStore
    {
        public List<string> Calls { get; } = [];
        public bool IsMissing { get; init; }
        public Exception? CompleteException { get; init; }
        public CancellationToken RollbackToken { get; private set; }

        public Task<BookDeletionPreparation?> BeginAsync(BookDeleteRequest request, CancellationToken cancellationToken)
        {
            Calls.Add("begin");
            return Task.FromResult(IsMissing
                ? null
                : new BookDeletionPreparation(
                    "operation-1",
                    new BookDeleteResult(request.BookId, request.DeleteAudioCache, 2, true)));
        }

        public Task CommitAsync(BookDeletionPreparation preparation, CancellationToken cancellationToken)
        {
            Calls.Add("commit");
            return Task.CompletedTask;
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
