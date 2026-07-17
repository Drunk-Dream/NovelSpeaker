using NovelSpeaker.Application.Books;

namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Assembles playback content from persisted metadata, stored text, segmentation, and regex rules.
/// </summary>
public sealed class BookPlaybackContentService : IBookPlaybackContentService
{
    private readonly IBookPlaybackMetadataQuery _metadataQuery;
    private readonly IBookContentReader _bookContentReader;
    private readonly ITextSegmenter _textSegmenter;
    private readonly ITextSegmentationOptionsProvider _optionsProvider;
    private readonly IRegexReplacementPipeline _regexReplacementPipeline;

    public BookPlaybackContentService(
        IBookPlaybackMetadataQuery metadataQuery,
        IBookContentReader bookContentReader,
        ITextSegmenter textSegmenter,
        ITextSegmentationOptionsProvider optionsProvider,
        IRegexReplacementPipeline regexReplacementPipeline)
    {
        _metadataQuery = metadataQuery;
        _bookContentReader = bookContentReader;
        _textSegmenter = textSegmenter;
        _optionsProvider = optionsProvider;
        _regexReplacementPipeline = regexReplacementPipeline;
    }

    public async Task<PlaybackBookContent?> GetBookAsync(string bookId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookId);

        var metadata = await _metadataQuery.GetBookAsync(bookId, cancellationToken).ConfigureAwait(false);
        if (metadata is null)
        {
            return null;
        }

        return new PlaybackBookContent(
            metadata.BookId,
            metadata.Title,
            metadata.Chapters
                .Select(chapter => PlaybackChapterContent.Unloaded(chapter.ChapterIndex, chapter.Title))
                .ToArray(),
            metadata.Author);
    }

    public async Task<PlaybackChapterContent?> GetChapterAsync(
        string bookId,
        int chapterIndex,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookId);

        var metadata = await _metadataQuery
            .GetChapterAsync(bookId, chapterIndex, cancellationToken)
            .ConfigureAwait(false);
        if (metadata is null)
        {
            return null;
        }

        var chapterText = await _bookContentReader.ReadChapterTextAsync(
            metadata.StoredFilePath,
            metadata.StartOffset,
            metadata.Length,
            cancellationToken).ConfigureAwait(false);
        var options = _optionsProvider.GetCurrent();
        var segments = await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return _textSegmenter.Segment(chapterText, options);
        }, cancellationToken).ConfigureAwait(false);
        var replaced = await _regexReplacementPipeline
            .ApplyAsync(segments, cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        return PlaybackChapterContent.FromLoaded(
            metadata.ChapterIndex,
            metadata.Title,
            replaced.Segments);
    }
}
