using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Cache;
using NovelSpeaker.Application.Playback.Cache;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Application.Speech.Compilation;
using NovelSpeaker.Domain.Books;
using NovelSpeaker.Domain.Settings;

namespace NovelSpeaker.Application.Playback.Export;

/// <summary>
/// Exports complete current-configuration chapter plans without rebuilding chapter text.
/// </summary>
public sealed class ExportChaptersService : IExportChaptersService
{
    private const int MaximumBookDirectoryNameLength = 80;
    private const int MaximumChapterTitleLength = 100;
    private const int MaximumFileNameBaseLength = 120;
    private readonly IBookPlaybackMetadataQuery _metadataQuery;
    private readonly IChapterSpeechPlanStore _speechPlanStore;
    private readonly IRegexReplacementRuleRepository _regexRuleRepository;
    private readonly ISelectedTtsRuleProvider _selectedRuleProvider;
    private readonly IAppSettingsService _settingsService;
    private readonly ExportFileNameSanitizer _fileNameSanitizer;
    private readonly IChapterMp3ExportWriter _writer;

    public ExportChaptersService(
        IBookPlaybackMetadataQuery metadataQuery,
        IChapterSpeechPlanStore speechPlanStore,
        IRegexReplacementRuleRepository regexRuleRepository,
        ISelectedTtsRuleProvider selectedRuleProvider,
        IAppSettingsService settingsService,
        ExportFileNameSanitizer fileNameSanitizer,
        IChapterMp3ExportWriter writer)
    {
        _metadataQuery = metadataQuery;
        _speechPlanStore = speechPlanStore;
        _regexRuleRepository = regexRuleRepository;
        _selectedRuleProvider = selectedRuleProvider;
        _settingsService = settingsService;
        _fileNameSanitizer = fileNameSanitizer;
        _writer = writer;
    }

    public Task<ExportChaptersResult> ExportAsync(
        ExportChaptersRequest request,
        CancellationToken cancellationToken) =>
        ExportAsync(request, progress: null, cancellationToken);

    public async Task<ExportChaptersResult> ExportAsync(
        ExportChaptersRequest request,
        IProgress<ExportChaptersProgress>? progress,
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

        TextProfileFingerprint currentTextProfile;
        try
        {
            var currentRules = await _regexRuleRepository
                .GetAllAsync(cancellationToken)
                .ConfigureAwait(false);
            currentTextProfile = TextProfileFingerprint.Create(
                settings.ToTextSegmentationOptions(),
                currentRules);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // Without a complete current text-profile snapshot we cannot safely
            // claim that a persisted plan represents the current configuration.
            return ExportChaptersResult.Failed(ExportChaptersStatus.ChapterSpeechPlanUnavailable);
        }

        var synthesisProfile = SynthesisProfileFingerprint.Create(
            TtsRuleFingerprint.Create(selectedRule.NormalizedRule),
            AppSettings.NormalizeSpeakSpeed(settings.DefaultSpeakSpeed));
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

            if (string.IsNullOrWhiteSpace(metadata.ChapterId))
            {
                return ExportChaptersResult.Failed(
                    ExportChaptersStatus.ChapterSpeechPlanUnavailable,
                    chapterIndex);
            }

            var plan = await _speechPlanStore
                .GetAsync(metadata.ChapterId, cancellationToken)
                .ConfigureAwait(false);
            if (plan is null ||
                plan.State != ChapterSpeechPlanState.Ready ||
                !plan.TextProfileFingerprint.Equals(currentTextProfile))
            {
                return ExportChaptersResult.Failed(
                    ExportChaptersStatus.ChapterSpeechPlanUnavailable,
                    chapterIndex);
            }

            var orderedSegments = plan.Segments
                .OrderBy(segment => segment.OrderIndex)
                .ToArray();
            if (plan.BodySegmentCount != orderedSegments.Length ||
                orderedSegments.Select(segment => segment.OrderIndex).Distinct().Count() != orderedSegments.Length ||
                orderedSegments.Any(segment =>
                    segment.SegmentKind != SpeechSegmentKind.Body ||
                    segment.SourceStartOffset < 0 ||
                    segment.SourceLength <= 0))
            {
                return ExportChaptersResult.Failed(
                    ExportChaptersStatus.ChapterSpeechPlanUnavailable,
                    chapterIndex);
            }

            var keys = orderedSegments
                .Select(segment => AudioCacheKey.FromSpeechTextHash(
                    metadata.ChapterId,
                    StableSpeechSegmentIdentity.Body(
                        segment.SourceStartOffset,
                        segment.SourceLength),
                    segment.SpeechTextHash,
                    synthesisProfile))
                .ToList();

            var chapterTitle = request.FrozenChapterTitles is not null &&
                               request.FrozenChapterTitles.TryGetValue(chapterIndex, out var frozenTitle)
                ? frozenTitle
                : metadata.Title;
            if (settings.ReadChapterTitle && !string.IsNullOrWhiteSpace(chapterTitle))
            {
                keys.Insert(
                    0,
                    AudioCacheKey.FromSpeechTextHash(
                        metadata.ChapterId,
                        StableSpeechSegmentIdentity.ChapterTitle(),
                        Fingerprint.Sha256(chapterTitle),
                        synthesisProfile));
            }

            if (keys.Count == 0)
            {
                return ExportChaptersResult.Failed(
                    ExportChaptersStatus.ChapterHasNoPlayableSegments,
                    chapterIndex);
            }

            var safeChapterTitle = _fileNameSanitizer.Sanitize(
                chapterTitle,
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
            _fileNameSanitizer.Sanitize(
                request.FrozenBookTitle ?? book.Title,
                MaximumBookDirectoryNameLength),
            plans,
            progress);
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
