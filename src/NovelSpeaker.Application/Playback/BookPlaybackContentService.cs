using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Playback.Cache;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Domain.Books;

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
    private readonly IAppSettingsService? _settingsService;
    private readonly IChapterSpeechPlanService? _speechPlanService;

    public BookPlaybackContentService(
        IBookPlaybackMetadataQuery metadataQuery,
        IBookContentReader bookContentReader,
        ITextSegmenter textSegmenter,
        ITextSegmentationOptionsProvider optionsProvider,
        IRegexReplacementPipeline regexReplacementPipeline,
        IAppSettingsService? settingsService = null,
        IChapterSpeechPlanService? speechPlanService = null)
    {
        _metadataQuery = metadataQuery;
        _bookContentReader = bookContentReader;
        _textSegmenter = textSegmenter;
        _optionsProvider = optionsProvider;
        _regexReplacementPipeline = regexReplacementPipeline;
        _settingsService = settingsService;
        _speechPlanService = speechPlanService;
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

        return await LoadChapterAsync(metadata, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PlaybackChapterContent>> GetChaptersAsync(
        string bookId,
        IReadOnlyCollection<int> chapterIndices,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookId);
        ArgumentNullException.ThrowIfNull(chapterIndices);

        var normalizedIndices = chapterIndices.Distinct().Order().ToArray();
        if (normalizedIndices.Length == 0)
        {
            return [];
        }

        var metadata = await _metadataQuery
            .GetChaptersAsync(bookId, normalizedIndices, cancellationToken)
            .ConfigureAwait(false);
        var chapters = new List<PlaybackChapterContent>(metadata.Count);
        foreach (var chapter in metadata)
        {
            cancellationToken.ThrowIfCancellationRequested();
            chapters.Add(await LoadChapterAsync(chapter, cancellationToken).ConfigureAwait(false));
        }

        return chapters;
    }

    private async Task<PlaybackChapterContent> LoadChapterAsync(
        PlaybackChapterMetadata metadata,
        CancellationToken cancellationToken)
    {
        var chapterText = await _bookContentReader.ReadChapterTextAsync(
            metadata.StoredFilePath,
            metadata.StartOffset,
            metadata.Length,
            cancellationToken).ConfigureAwait(false);
        var options = _optionsProvider.GetCurrent();
        IReadOnlyList<SpeechSegment> replacedSegments;
        if (_speechPlanService is not null && metadata.ChapterId is not null)
        {
            var planResult = await _speechPlanService
                .BuildAsync(metadata.ChapterId, chapterText, options, cancellationToken)
                .ConfigureAwait(false);
            replacedSegments = planResult.Segments;
        }
        else
        {
            var segments = await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return _textSegmenter.Segment(chapterText, options);
            }, cancellationToken).ConfigureAwait(false);
            var replaced = await _regexReplacementPipeline
                .ApplyAsync(segments, cancellationToken)
                .ConfigureAwait(false);
            replacedSegments = replaced.Segments;
        }
        cancellationToken.ThrowIfCancellationRequested();

        var playbackSegments = PlaybackSpeechSegmentComposer.Compose(
            metadata.Title,
            replacedSegments,
            _settingsService?.Current.ReadChapterTitle == true);

        return PlaybackChapterContent.FromLoaded(
            metadata.ChapterIndex,
            metadata.Title,
            playbackSegments);
    }
}
