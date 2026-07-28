namespace NovelSpeaker.Application.Abstractions;

/// <summary>
/// Provides cancellable metadata and text operations for files explicitly selected by the user.
/// The caller owns the selected path; implementations do not widen access beyond that path.
/// </summary>
public interface IUserDocumentFileOperations
{
    Task<UserDocumentFileMetadata?> GetMetadataAsync(
        string filePath,
        CancellationToken cancellationToken);

    Task<string> ReadTextAsync(
        string filePath,
        CancellationToken cancellationToken);

    Task WriteTextAsync(
        string filePath,
        string content,
        CancellationToken cancellationToken);
}
