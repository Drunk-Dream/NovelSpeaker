namespace NovelSpeaker.Application.Books;

/// <summary>Removes one bound source, deleting its book when it is the last source.</summary>
public interface IBookSourceRemovalService
{
    Task<BookSourceRemoveResult?> RemoveAsync(BookSourceRemoveRequest request, CancellationToken cancellationToken);
}

public sealed record BookSourceRemoveRequest(string BookId, string SourceId);

public sealed record BookSourceRemoveResult(string BookId, string SourceId, bool DeletedBook);
