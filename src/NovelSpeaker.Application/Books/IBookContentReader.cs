namespace NovelSpeaker.Application.Books;

/// <summary>
/// Reads chapter text slices from one persisted normalized book content file.
/// </summary>
public interface IBookContentReader
{
    Task<string> ReadChapterTextAsync(
        string storedFilePath,
        int startOffset,
        int length,
        CancellationToken cancellationToken);
}
