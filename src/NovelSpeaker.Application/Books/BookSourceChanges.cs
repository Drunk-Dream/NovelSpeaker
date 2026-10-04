namespace NovelSpeaker.Application.Books;

/// <summary>Books-owned committed changes; each observer is isolated from the mutation and other observers.</summary>
public sealed class BookSourceChanges : IBookSourceChangeSource
{
    public event EventHandler<BookCommittedChange>? Changed;

    internal void Publish(BookCommittedChange change)
    {
        foreach (EventHandler<BookCommittedChange> handler in Changed?.GetInvocationList() ?? [])
        {
            try { handler(this, change); }
            catch { /* Observers cannot fail a durable mutation. */ }
        }
    }
}
