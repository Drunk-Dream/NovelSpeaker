namespace NovelSpeaker.Application.Books;

/// <summary>
/// Reads the independent projections needed by the book-details experience.
/// </summary>
public interface IBookDetailsQuery
{
    Task<BookDetailsHeader?> GetHeaderAsync(string bookId, CancellationToken cancellationToken);

    Task<IReadOnlyList<BookChapterSummary>> GetCatalogAsync(
        string bookId,
        CancellationToken cancellationToken);

    Task<BookReadingPosition?> GetReadingPositionAsync(
        string bookId,
        CancellationToken cancellationToken);

    Task<BookDetailsStatistics?> GetStatisticsAsync(
        string bookId,
        CancellationToken cancellationToken);
}
