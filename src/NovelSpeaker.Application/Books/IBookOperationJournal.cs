namespace NovelSpeaker.Application.Books;

/// <summary>
/// Persists cross-storage book operation intent and phase transitions for startup recovery.
/// </summary>
public interface IBookOperationJournal
{
    Task CreateAsync(BookOperationRecord operation, CancellationToken cancellationToken);

    Task SetPhaseAsync(string operationId, BookOperationPhase phase, CancellationToken cancellationToken);

    Task<IReadOnlyList<BookOperationRecord>> GetIncompleteAsync(CancellationToken cancellationToken);
}
