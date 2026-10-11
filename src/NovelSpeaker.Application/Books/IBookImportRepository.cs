using NovelSpeaker.Domain.Books;

namespace NovelSpeaker.Application.Books;

/// <summary>
/// Resolves the unique normalized book identity and atomically commits a local binding
/// plus its complete CurrentCatalog when active. No fuzzy candidates or identity edits.
/// </summary>
public interface IBookImportRepository
{
    Task<LocalSourceImportTarget?> FindByIdentityAsync(BookIdentity identity, CancellationToken cancellationToken);
    Task<LocalSourceImportTarget?> GetTargetAsync(string bookId, CancellationToken cancellationToken);
    Task SaveAsync(LocalSourceImportSnapshot snapshot, string operationId, CancellationToken cancellationToken);
}
