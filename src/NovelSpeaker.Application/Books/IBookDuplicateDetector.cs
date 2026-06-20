namespace NovelSpeaker.Application.Books;

/// <summary>
/// Looks up existing imported books by source hash.
/// </summary>
public interface IBookDuplicateDetector
{
    Task<string?> FindExistingBookIdAsync(string sourceHash, CancellationToken cancellationToken);
}
