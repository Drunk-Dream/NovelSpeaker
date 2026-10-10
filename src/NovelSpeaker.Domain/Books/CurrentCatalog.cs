namespace NovelSpeaker.Domain.Books;

/// <summary>
/// One complete immutable catalog snapshot for a book's active binding.
/// Inactive bindings retain content, not catalogs. The persistence boundary must
/// validate the active binding and replace the complete snapshot atomically.
/// </summary>
public sealed class CurrentCatalog
{
    public CurrentCatalog(string bookId, string sourceBindingId, IReadOnlyList<Chapter> entries)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceBindingId);
        ArgumentNullException.ThrowIfNull(entries);
        var snapshot = entries.ToArray();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < snapshot.Length; index++)
        {
            var entry = snapshot[index];
            if (entry is null || entry.BookId != bookId || entry.SourceBindingId != sourceBindingId ||
                entry.ChapterIndex != index ||
                string.IsNullOrWhiteSpace(entry.Id) || !ids.Add(entry.Id))
                throw new ArgumentException("Entries must form one ordered book catalog with unique technical IDs.", nameof(entries));
        }

        BookId = bookId;
        SourceBindingId = sourceBindingId;
        Entries = Array.AsReadOnly(snapshot);
    }

    public string BookId { get; }
    public string SourceBindingId { get; }
    public IReadOnlyList<Chapter> Entries { get; }
}
