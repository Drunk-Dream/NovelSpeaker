using System.Text;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Cache;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Application.Speech.Compilation;
using NovelSpeaker.Domain.Books;
using NovelSpeaker.Domain.Settings;

namespace NovelSpeaker.Application.Playback.Cache;

/// <summary>
/// Composes the current TTS/text configuration with persisted speech-plan and cache facts.
/// This class is deliberately read-only: repair orchestration is a separate owner.
/// </summary>
public sealed class CacheCoverageQuery : ICacheCoverageQuery
{
    private readonly IAudioCacheStore _cacheStore;
    private readonly IBookPlaybackMetadataQuery _metadataQuery;
    private readonly ISelectedTtsRuleProvider _selectedRuleProvider;
    private readonly IAppSettingsService _settingsService;
    private readonly IRegexReplacementRuleRepository? _regexRuleRepository;
    private readonly ICacheWorkspaceFailureReporter? _failureReporter;

    public CacheCoverageQuery(
        IAudioCacheStore cacheStore,
        IBookPlaybackMetadataQuery metadataQuery,
        ISelectedTtsRuleProvider selectedRuleProvider,
        IAppSettingsService settingsService,
        IRegexReplacementRuleRepository? regexRuleRepository = null,
        ICacheWorkspaceFailureReporter? failureReporter = null)
    {
        _cacheStore = cacheStore;
        _metadataQuery = metadataQuery;
        _selectedRuleProvider = selectedRuleProvider;
        _settingsService = settingsService;
        _regexRuleRepository = regexRuleRepository;
        _failureReporter = failureReporter;
    }

    public async Task<IReadOnlyList<ChapterCacheStatus>> GetAsync(
        string bookId,
        IReadOnlyCollection<int> chapterIndices,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookId);
        ArgumentNullException.ThrowIfNull(chapterIndices);

        var normalizedIndices = NormalizeChapterIndices(chapterIndices);
        if (normalizedIndices.Length == 0)
        {
            return [];
        }

        var selectedRule = await _selectedRuleProvider
            .GetSelectedRuleAsync(cancellationToken)
            .ConfigureAwait(false);
        if (selectedRule is null)
        {
            return normalizedIndices
                .Select(CreateConfigurationUnavailable)
                .ToArray();
        }

        try
        {
            var chapters = await _metadataQuery
                .GetChaptersAsync(bookId, normalizedIndices, cancellationToken)
                .ConfigureAwait(false);
            return await GetAsyncCore(
                bookId,
                normalizedIndices,
                chapters,
                selectedRule,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsExpectedCompletenessFailure(exception))
        {
            ReportCompletenessFailure(exception);
            return normalizedIndices
                .Select(CreateConfigurationUnavailable)
                .ToArray();
        }
    }

    public async Task<IReadOnlyList<ChapterCacheStatus>> GetAsync(
        string bookId,
        IReadOnlyCollection<int> chapterIndices,
        IReadOnlyCollection<PlaybackChapterMetadata> chapters,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookId);
        ArgumentNullException.ThrowIfNull(chapterIndices);
        ArgumentNullException.ThrowIfNull(chapters);

        var normalizedIndices = NormalizeChapterIndices(chapterIndices);
        if (normalizedIndices.Length == 0)
        {
            return [];
        }

        var selectedRule = await _selectedRuleProvider
            .GetSelectedRuleAsync(cancellationToken)
            .ConfigureAwait(false);
        if (selectedRule is null)
        {
            return normalizedIndices
                .Select(CreateConfigurationUnavailable)
                .ToArray();
        }

        try
        {
            return await GetAsyncCore(
                bookId,
                normalizedIndices,
                chapters,
                selectedRule,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsExpectedCompletenessFailure(exception))
        {
            ReportCompletenessFailure(exception);
            return normalizedIndices
                .Select(CreateConfigurationUnavailable)
                .ToArray();
        }
    }

    private async Task<IReadOnlyList<ChapterCacheStatus>> GetAsyncCore(
        string bookId,
        IReadOnlyCollection<int> normalizedIndices,
        IReadOnlyCollection<PlaybackChapterMetadata> chapters,
        SelectedPlaybackRule selectedRule,
        CancellationToken cancellationToken)
    {
        var settings = _settingsService.Current;
        var textProfile = await GetCurrentTextProfileAsync(settings, cancellationToken)
            .ConfigureAwait(false);
        var synthesisProfile = SynthesisProfileFingerprint.Create(
            TtsRuleFingerprint.Create(selectedRule.NormalizedRule),
            AppSettings.NormalizeSpeakSpeed(settings.DefaultSpeakSpeed));
        var queries = chapters
            .Where(chapter => !string.IsNullOrWhiteSpace(chapter.ChapterId))
            .Select(chapter => new CurrentCacheChapterQuery(
                chapter.ChapterId!,
                chapter.ChapterIndex,
                settings.ReadChapterTitle,
                settings.ReadChapterTitle && NarratableText.HasContent(chapter.Title)
                    ? Fingerprint.Sha256(chapter.Title)
                    : null,
                textProfile))
            .ToArray();
        var queriedStatuses = queries.Length == 0
            ? []
            : await _cacheStore
                .GetCurrentConfigurationStatusesAsync(
                    queries,
                    synthesisProfile,
                    cancellationToken)
                .ConfigureAwait(false);
        var statusesByIndex = queriedStatuses.ToDictionary(status => status.ChapterIndex);
        return normalizedIndices
            .Select(index => statusesByIndex.GetValueOrDefault(index, CreateConfigurationUnavailable(index)))
            .ToArray();
    }

    private async Task<TextProfileFingerprint> GetCurrentTextProfileAsync(
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<RegexReplacementRule> rules = _regexRuleRepository is null
            ? Array.Empty<RegexReplacementRule>()
            : await _regexRuleRepository
                .GetAllAsync(cancellationToken)
                .ConfigureAwait(false);
        return TextProfileFingerprint.Create(settings.ToTextSegmentationOptions(), rules);
    }

    private static int[] NormalizeChapterIndices(IReadOnlyCollection<int> chapterIndices)
    {
        var normalized = chapterIndices.Distinct().Order().ToArray();
        if (normalized.Length > 0 && normalized[0] < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chapterIndices));
        }

        return normalized;
    }

    private static ChapterCacheStatus CreateConfigurationUnavailable(int chapterIndex) =>
        new(chapterIndex, 0, null)
        {
            Kind = ChapterCacheStatusKind.ConfigurationUnavailable
        };

    private void ReportCompletenessFailure(Exception exception)
    {
        try
        {
            _failureReporter?.ReportCompletenessUnavailable(exception);
        }
        catch
        {
            // Diagnostics are best effort and must not replace a read-only query result.
        }
    }

    private static bool IsExpectedCompletenessFailure(Exception exception) =>
        exception is FileNotFoundException or
            DirectoryNotFoundException or
            UnauthorizedAccessException or
            ArgumentOutOfRangeException or
            IOException or
            DecoderFallbackException or
            InvalidDataException;
}
