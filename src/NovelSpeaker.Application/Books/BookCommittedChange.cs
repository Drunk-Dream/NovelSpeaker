namespace NovelSpeaker.Application.Books;

/// <summary>
/// Books-owned durable facts. Consumers requery their own read models; no mutable snapshots are published.
/// Notifications describe the commit, even when subsequent journal/file cleanup needs recovery.
/// </summary>
public abstract record BookCommittedChange
{
    private protected BookCommittedChange(string bookId) => BookId = bookId;

    public string BookId { get; init; }

    /// <summary>The Book display metadata was persisted, including initial creation.</summary>
    public sealed record MetadataCommitted(string BookId) : BookCommittedChange(BookId);

    /// <summary>The active Source's complete catalog was persisted. Version is its first technical ChapterId.</summary>
    public sealed record ActiveCatalogCommitted(string BookId, string SourceId, string CatalogVersion) : BookCommittedChange(BookId);

    /// <summary>A surviving/new Book's active Source changed; null means cleared without fallback.</summary>
    public sealed record ActiveSourceChanged(string BookId, string? PreviousSourceId, string? SourceId) : BookCommittedChange(BookId);

    /// <summary>An explicitly removed Source was committed, including removal of the last Source.</summary>
    public sealed record SourceRemoved(string BookId, string SourceId) : BookCommittedChange(BookId);

    /// <summary>The Book and all its Sources were removed. No ActiveSource clear is published for a deleted Book.</summary>
    public sealed record BookRemoved(string BookId) : BookCommittedChange(BookId);
}
