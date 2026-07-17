namespace NovelSpeaker.Application.Books;

/// <summary>
/// Validates and updates user-editable book metadata.
/// </summary>
public interface IBookMetadataUpdateService
{
    Task<BookDetailsHeader> UpdateMetadataAsync(BookMetadataUpdateRequest request, CancellationToken cancellationToken);
}
