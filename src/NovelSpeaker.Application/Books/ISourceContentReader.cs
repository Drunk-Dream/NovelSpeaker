namespace NovelSpeaker.Application.Books;

/// <summary>
/// Reads source-owned content without exposing typed storage details to consumers.
/// </summary>
public interface ISourceContentReader
{
    Task<string> ReadChapterTextAsync(
        string sourceId,
        string chapterId,
        CancellationToken cancellationToken);

    Task<string> ReadSourceTextAsync(string sourceId, CancellationToken cancellationToken);
}
