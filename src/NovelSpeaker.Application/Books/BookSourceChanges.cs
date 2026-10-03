namespace NovelSpeaker.Application.Books;

/// <summary>Books-owned committed catalog changes for import and removal.</summary>
public sealed class BookSourceChanges : IBookSourceChangeSource
{
    public event EventHandler<BookSourceCatalogChanged>? CatalogChanged;

    public void Publish(BookSourceCatalogChanged change)
    {
        foreach (EventHandler<BookSourceCatalogChanged> handler in CatalogChanged?.GetInvocationList() ?? [])
        {
            try { handler(this, change); }
            catch { /* Observers cannot fail a durable mutation. */ }
        }
    }
}
