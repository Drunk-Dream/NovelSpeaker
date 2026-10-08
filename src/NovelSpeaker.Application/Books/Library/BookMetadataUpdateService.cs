namespace NovelSpeaker.Application.Books.Library;

/// <summary>Owns metadata validation, serialized persistence and committed change publication.</summary>
public sealed class BookMetadataUpdateService(
    IBookMetadataStore store, BookMutationGate mutations, BookSourceChanges changes) : IBookMetadataUpdateService
{
    public Task<BookDetailsHeader> UpdateMetadataAsync(BookMetadataUpdateRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var title = request.Title.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new InvalidOperationException("书名不能为空。");
        }

        var normalized = request with { Title = title, Author = string.IsNullOrWhiteSpace(request.Author) ? null : request.Author.Trim() };
        // SQLite may complete synchronously; share the Books gate without blocking the UI thread.
        return mutations.RunAsync(() => Task.Run(async () =>
        {
            var header = await store.UpdateAsync(normalized, cancellationToken).ConfigureAwait(false);
            changes.Publish(new BookCommittedChange.MetadataCommitted(header.Id));
            return header;
        }, cancellationToken), cancellationToken);
    }
}
