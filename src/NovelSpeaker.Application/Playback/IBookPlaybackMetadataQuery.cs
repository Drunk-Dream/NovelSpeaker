namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Queries persisted playback metadata without reading or assembling chapter text.
/// </summary>
public interface IBookPlaybackMetadataQuery
{
    Task<PlaybackBookMetadata?> GetBookAsync(string bookId, CancellationToken cancellationToken);

    Task<PlaybackChapterMetadata?> GetChapterAsync(
        string bookId,
        int chapterIndex,
        CancellationToken cancellationToken);

    async Task<IReadOnlyList<PlaybackChapterMetadata>> GetChaptersAsync(
        string bookId,
        IReadOnlyCollection<int> chapterIndices,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookId);
        ArgumentNullException.ThrowIfNull(chapterIndices);

        var chapters = new List<PlaybackChapterMetadata>(chapterIndices.Count);
        foreach (var chapterIndex in chapterIndices.Distinct().Order())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var chapter = await GetChapterAsync(bookId, chapterIndex, cancellationToken).ConfigureAwait(false);
            if (chapter is not null)
            {
                chapters.Add(chapter);
            }
        }

        return chapters;
    }
}
