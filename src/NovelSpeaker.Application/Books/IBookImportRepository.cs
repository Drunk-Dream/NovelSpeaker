using NovelSpeaker.Domain.Books;

namespace NovelSpeaker.Application.Books;

/// <summary>
/// Commits an imported book and all of its chapters as one persistence operation.
/// </summary>
public interface IBookImportRepository
{
    Task SaveAsync(Book book, IReadOnlyList<Chapter> chapters, CancellationToken cancellationToken);
}
