namespace NovelSpeaker.Application.Books;

/// <summary>Persists normalized Book display metadata and returns the header after successful commit.</summary>
public interface IBookMetadataStore
{
    Task<BookDetailsHeader> UpdateAsync(BookMetadataUpdateRequest request, CancellationToken cancellationToken);
}
