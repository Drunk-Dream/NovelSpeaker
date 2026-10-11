namespace NovelSpeaker.Infrastructure.Persistence;

/// <summary>A library cannot be upgraded without guessing identities or discarding formal data.</summary>
public sealed class IncompatibleBookLibraryException : IOException
{
    public IncompatibleBookLibraryException()
        : base("The book library cannot be migrated safely and requires reimport.")
    {
    }
}
