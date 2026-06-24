namespace NovelSpeaker.Application.Books;

/// <summary>
/// Computes deterministic content hashes used for duplicate detection.
/// </summary>
public interface IContentHasher
{
    Task<string> ComputeFileHashAsync(
        string filePath,
        IProgress<BookImportProgress>? progress,
        CancellationToken cancellationToken);
}
