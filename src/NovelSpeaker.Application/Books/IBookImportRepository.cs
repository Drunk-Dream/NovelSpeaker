using NovelSpeaker.Domain.Books;

namespace NovelSpeaker.Application.Books;

/// <summary>
/// Writes imported books and chapters in one SQLite transaction.
/// </summary>
public interface IBookImportRepository
{
    Task SaveAsync(Book book, IReadOnlyList<Chapter> chapters, CancellationToken cancellationToken);
}
