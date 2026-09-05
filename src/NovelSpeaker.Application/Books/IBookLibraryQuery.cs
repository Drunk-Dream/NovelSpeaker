namespace NovelSpeaker.Application.Books;

/// <summary>
/// Reads detached book summaries for library experiences.
/// </summary>
public interface IBookLibraryQuery
{
    Task<IReadOnlyList<BookSummary>> GetBooksAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<BookSummary>> GetBooksAsync(
        IReadOnlyCollection<string> bookIds,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("Targeted book queries are not supported by this implementation.");
}
