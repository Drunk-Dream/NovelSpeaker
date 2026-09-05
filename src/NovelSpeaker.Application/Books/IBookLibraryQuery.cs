namespace NovelSpeaker.Application.Books;

/// <summary>
/// Reads detached book summaries for library experiences.
/// </summary>
public interface IBookLibraryQuery
{
    Task<IReadOnlyList<BookSummary>> GetBooksAsync(CancellationToken cancellationToken);
}
