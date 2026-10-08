using System.Text;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Cache;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Domain.Books;
using NovelSpeaker.Application.Books.TextProcessing;

namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Assembles playback content from persisted metadata, stored text, segmentation, and regex rules.
/// </summary>
internal sealed class PlaybackContentResolver : IBookPlaybackContentService
{
    private readonly IBookPlaybackMetadataQuery _metadataQuery;
    private readonly ISourceContentReader _sourceContentReader;
    private readonly ITextSegmenter _textSegmenter;
    private readonly ITextSegmentationOptionsProvider _optionsProvider;
    private readonly IRegexReplacementPipeline _regexReplacementPipeline;
    private readonly IAppSettingsService? _settingsService;
    private readonly IChapterSpeechPlanService? _speechPlanService;
    private readonly IBookPlaybackContentFailureReporter? _failureReporter;
    private readonly IRegexReplacementRuleRepository? _regexRules;

    public PlaybackContentResolver(
        IBookPlaybackMetadataQuery metadataQuery,
        ISourceContentReader sourceContentReader,
        ITextSegmenter textSegmenter,
        ITextSegmentationOptionsProvider optionsProvider,
        IRegexReplacementPipeline regexReplacementPipeline,
        IAppSettingsService? settingsService = null,
        IChapterSpeechPlanService? speechPlanService = null,
        IBookPlaybackContentFailureReporter? failureReporter = null,
        IRegexReplacementRuleRepository? regexRules = null)
    {
        _metadataQuery = metadataQuery;
        _sourceContentReader = sourceContentReader;
        _textSegmenter = textSegmenter;
        _optionsProvider = optionsProvider;
        _regexReplacementPipeline = regexReplacementPipeline;
        _settingsService = settingsService;
        _speechPlanService = speechPlanService;
        _failureReporter = failureReporter;
        _regexRules = regexRules;
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
                .Select(chapter => PlaybackChapterContent.Unloaded(chapter.ChapterIndex, chapter.Title, chapter.ChapterId))
                .ToArray(),
            metadata.Author,
            metadata.SourceContext);
    }

    public async Task<PlaybackBookContent?> GetBookAsync(
        string bookId,
        IReadOnlyList<BookChapterSummary> catalog,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookId);
        ArgumentNullException.ThrowIfNull(catalog);

        // A page's catalog may predate a source update. Runtime navigation always uses
        // one current source snapshot, including its technical IDs.
        return await GetBookAsync(bookId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> IsCurrentAsync(PlaybackBookContent book, CancellationToken cancellationToken)
    {
        var header = await _metadataQuery.GetBookHeaderAsync(book.BookId, cancellationToken).ConfigureAwait(false);
        return header is not null && header.SourceContext == book.SourceContext;
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

        try
        {
            var loaded = await LoadChapterAsync(metadata, cancellationToken).ConfigureAwait(false);
            await EnsureCurrentAsync(bookId, metadata.SourceContext, cancellationToken).ConfigureAwait(false);
            return loaded;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsExpectedContentFailure(exception))
        {
            ReportContentFailure(exception);
            throw;
        }
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

        var options = _optionsProvider.GetCurrent();
        var readTitle = _settingsService?.Current.ReadChapterTitle == true;
        var rules = _regexRules is null ? null :
            (await _regexRules.GetAllAsync(cancellationToken).ConfigureAwait(false)).ToArray();
        var metadata = await _metadataQuery
            .GetChaptersAsync(bookId, normalizedIndices, cancellationToken)
            .ConfigureAwait(false);
        var chapters = new List<PlaybackChapterContent>(metadata.Count);
        foreach (var chapter in metadata)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                chapters.Add(await LoadChapterAsync(chapter, cancellationToken, options, readTitle, rules).ConfigureAwait(false));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (IsExpectedContentFailure(exception))
            {
                ReportContentFailure(exception);
                throw;
            }
        }

        if (metadata.Count > 0)
            await EnsureCurrentAsync(bookId, metadata[0].SourceContext, cancellationToken).ConfigureAwait(false);
        return chapters;
    }

    private async Task EnsureCurrentAsync(string bookId, ActiveSourceContext? context, CancellationToken cancellationToken)
    {
        var header = await _metadataQuery.GetBookHeaderAsync(bookId, cancellationToken).ConfigureAwait(false);
        if (header is null || header.SourceContext != context)
            throw new OperationCanceledException("活动来源目录已更新。", cancellationToken);
    }

    private void ReportContentFailure(Exception exception)
    {
        try
        {
            _failureReporter?.ReportChapterReadFailure(exception);
        }
        catch
        {
            // Failure diagnostics are best effort and must not replace the read failure.
        }
    }

    private static bool IsExpectedContentFailure(Exception exception)
    {
        return exception is FileNotFoundException or
            DirectoryNotFoundException or
            UnauthorizedAccessException or
            ArgumentOutOfRangeException or
            IOException or
            DecoderFallbackException or
            InvalidDataException;
    }

    private async Task<PlaybackChapterContent> LoadChapterAsync(
        PlaybackChapterMetadata metadata,
        CancellationToken cancellationToken,
        TextSegmentationOptions? frozenOptions = null,
        bool? frozenReadTitle = null,
        IReadOnlyList<RegexReplacementRule>? frozenRules = null)
    {
        var options = frozenOptions ?? _optionsProvider.GetCurrent();
        var readTitle = frozenReadTitle ?? _settingsService?.Current.ReadChapterTitle == true;
        var chapterText = await _sourceContentReader.ReadChapterTextAsync(
            metadata.SourceId,
            metadata.ChapterId,
            cancellationToken).ConfigureAwait(false);
        IReadOnlyList<SpeechSegment> replacedSegments;
        if (_speechPlanService is not null && metadata.ChapterId is not null)
        {
            var planResult = await _speechPlanService
                .BuildAsync(metadata.ChapterId, chapterText, options, cancellationToken, frozenRules)
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
            var replaced = frozenRules is null
                ? await _regexReplacementPipeline.ApplyAsync(segments, cancellationToken).ConfigureAwait(false)
                : RegexReplacementProcessor.Apply(segments, frozenRules, cancellationToken);
            replacedSegments = replaced.Segments;
        }
        cancellationToken.ThrowIfCancellationRequested();

        var playbackSegments = PlaybackSpeechSegmentComposer.Compose(
            metadata.Title,
            replacedSegments,
            readTitle);

        return PlaybackChapterContent.FromLoaded(
            metadata.ChapterIndex,
            metadata.Title,
            playbackSegments,
            metadata.ChapterId);
    }
}
