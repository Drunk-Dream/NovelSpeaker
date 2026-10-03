namespace NovelSpeaker.Application.Books;

/// <summary>
/// Resolves book candidates and atomically commits a complete local source snapshot.
/// </summary>
public interface IBookImportRepository
{
    Task<IReadOnlyList<BookImportCandidate>> FindCandidatesAsync(string title, string? author, CancellationToken cancellationToken);
    Task<LocalSourceImportTarget?> GetTargetAsync(string bookId, CancellationToken cancellationToken);
    Task SaveAsync(LocalSourceImportSnapshot snapshot, string operationId, CancellationToken cancellationToken);
}
