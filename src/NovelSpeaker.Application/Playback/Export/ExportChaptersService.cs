using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Books.TextProcessing;
using NovelSpeaker.Application.Playback.Cache;
using NovelSpeaker.Application.Settings;

namespace NovelSpeaker.Application.Playback.Export;

/// <summary>
/// Freezes the current playback identity and text configuration, then delegates technical MP3 publication.
/// </summary>
public sealed class ExportChaptersService : IExportChaptersService
{
    private const int MaximumBookDirectoryNameLength = 80;
    private const int MaximumChapterTitleLength = 100;
    private const int MaximumFileNameBaseLength = 120;
    private readonly IBookPlaybackMetadataQuery _metadataQuery;
    private readonly IBookContentReader _contentReader;
    private readonly ITextSegmenter _textSegmenter;
    private readonly IRegexReplacementRuleRepository _regexRuleRepository;
    private readonly ISelectedTtsRuleProvider _selectedRuleProvider;
    private readonly IAppSettingsService _settingsService;
    private readonly ExportFileNameSanitizer _fileNameSanitizer;
    private readonly IChapterMp3ExportWriter _writer;

    public ExportChaptersService(
        IBookPlaybackMetadataQuery metadataQuery,
        IBookContentReader contentReader,
        ITextSegmenter textSegmenter,
        IRegexReplacementRuleRepository regexRuleRepository,
        ISelectedTtsRuleProvider selectedRuleProvider,
        IAppSettingsService settingsService,
        ExportFileNameSanitizer fileNameSanitizer,
        IChapterMp3ExportWriter writer)
    {
        _metadataQuery = metadataQuery;
        _contentReader = contentReader;
        _textSegmenter = textSegmenter;
        _regexRuleRepository = regexRuleRepository;
        _selectedRuleProvider = selectedRuleProvider;
        _settingsService = settingsService;
        _fileNameSanitizer = fileNameSanitizer;
        _writer = writer;
    }

    public async Task<ExportChaptersResult> ExportAsync(
        ExportChaptersRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.BookId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DestinationRootDirectory);
        ArgumentNullException.ThrowIfNull(request.ChapterIndices);

        var chapterIndices = request.ChapterIndices
            .Distinct()
            .Order()
            .ToArray();
        if (chapterIndices.Length == 0)
        {
            throw new ArgumentException("At least one chapter must be selected.", nameof(request));
        }

        if (chapterIndices[0] < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var settings = _settingsService.Current;
        var selectedRule = await _selectedRuleProvider
            .GetSelectedRuleAsync(cancellationToken)
            .ConfigureAwait(false);
        if (selectedRule is null ||
            settings.SelectedTtsRuleId is null ||
            selectedRule.RuleId != settings.SelectedTtsRuleId.Value)
        {
            return ExportChaptersResult.Failed(ExportChaptersStatus.SelectedRuleUnavailable);
        }

        var book = await _metadataQuery
            .GetBookAsync(request.BookId, cancellationToken)
            .ConfigureAwait(false);
        if (book is null)
        {
            return ExportChaptersResult.Failed(ExportChaptersStatus.BookNotFound);
        }

        var regexRules = (await _regexRuleRepository
                .GetAllAsync(cancellationToken)
                .ConfigureAwait(false))
            .Where(rule => rule.IsEnabled)
            .OrderBy(rule => rule.SortOrder)
            .ThenBy(rule => rule.Id)
            .ToArray();
        var segmentationOptions = settings.ToTextSegmentationOptions();
        var plans = new List<ChapterMp3ExportPlan>(chapterIndices.Length);
        foreach (var chapterIndex in chapterIndices)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var metadata = await _metadataQuery
                .GetChapterAsync(request.BookId, chapterIndex, cancellationToken)
                .ConfigureAwait(false);
            if (metadata is null)
            {
                return ExportChaptersResult.Failed(
                    ExportChaptersStatus.ChapterNotFound,
                    chapterIndex);
            }

            var chapterText = await _contentReader
                .ReadChapterTextAsync(
                    metadata.StoredFilePath,
                    metadata.StartOffset,
                    metadata.Length,
                    cancellationToken)
                .ConfigureAwait(false);
            var sourceSegments = await Task.Run(
                () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return _textSegmenter.Segment(chapterText, segmentationOptions);
                },
                cancellationToken).ConfigureAwait(false);
            var processed = RegexReplacementProcessor.Apply(sourceSegments, regexRules, cancellationToken);
            var playbackSegments = PlaybackSpeechSegmentComposer.Compose(
                metadata.Title,
                processed.Segments,
                settings.ReadChapterTitle);
            var keys = playbackSegments
                .Where(segment => !string.IsNullOrWhiteSpace(segment.SpeechText))
                .Select(segment => AudioCacheKey.FromPlayback(
                    request.BookId,
                    chapterIndex,
                    segment.SegmentIndex,
                    selectedRule.RuleId,
                    settings.DefaultSpeakSpeed,
                    segment.SpeechText))
                .ToArray();
            if (keys.Length == 0)
            {
                return ExportChaptersResult.Failed(
                    ExportChaptersStatus.ChapterHasNoPlayableSegments,
                    chapterIndex);
            }

            var safeChapterTitle = _fileNameSanitizer.Sanitize(
                metadata.Title,
                MaximumChapterTitleLength);
            plans.Add(new ChapterMp3ExportPlan(
                chapterIndex,
                _fileNameSanitizer.Sanitize(
                    $"{chapterIndex + 1:D3}_{safeChapterTitle}",
                    MaximumFileNameBaseLength),
                keys));
        }

        var batch = new ChapterMp3ExportBatch(
            request.DestinationRootDirectory,
            _fileNameSanitizer.Sanitize(book.Title, MaximumBookDirectoryNameLength),
            plans);
        var writeResult = await _writer
            .WriteAsync(batch, cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return writeResult.Status == ChapterMp3ExportWriteStatus.IncompleteCache
            ? ExportChaptersResult.Failed(
                ExportChaptersStatus.IncompleteCache,
                writeResult.IncompleteChapterIndex)
            : new ExportChaptersResult(
                ExportChaptersStatus.Succeeded,
                writeResult.ExportDirectoryPath,
                writeResult.Files,
                null);
    }
}
