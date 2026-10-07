using NovelSpeaker.Application.Speech.Providers;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Domain.Settings;

namespace NovelSpeaker.Application.Cache;

/// <summary>
/// Maps typed source changes to Cache invalidation without making source modules Cache-aware.
/// </summary>
internal sealed class CacheConfigurationChangeObserver : IDisposable
{
    private readonly IAppSettingsService _settingsService;
    private readonly IRegexReplacementRuleWorkspaceService? _regexWorkspace;
    private readonly Action<CacheInvalidation> _publish;
    private readonly ICurrentSpeechProvider? _providers;
    private readonly IBookSourceChangeSource? _books;
    private bool _disposed;

    public CacheConfigurationChangeObserver(
        IAppSettingsService settingsService,
        IRegexReplacementRuleWorkspaceService? regexWorkspace,
        Action<CacheInvalidation> publish,
        ICurrentSpeechProvider? providers = null,
        IBookSourceChangeSource? books = null)
    {
        _settingsService = settingsService;
        _regexWorkspace = regexWorkspace;
        _publish = publish;
        _providers = providers;
        _books = books;
        if (_books is not null) _books.Changed += OnBookChanged;
        if (providers is not null) providers.Changed += OnProvidersChanged;

        _settingsService.Changed += OnSettingsChanged;
        if (_regexWorkspace is not null)
        {
            _regexWorkspace.Changed += OnRegexRulesChanged;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _settingsService.Changed -= OnSettingsChanged;
        if (_books is not null) _books.Changed -= OnBookChanged;
        if (_providers is not null) _providers.Changed -= OnProvidersChanged;
        if (_regexWorkspace is not null)
        {
            _regexWorkspace.Changed -= OnRegexRulesChanged;
        }
    }

    private void OnProvidersChanged(object? sender, SpeechProvidersChangedEventArgs e)
    {
        if (e.AffectsSynthesis) PublishCoverageInvalidation();
    }

    private void OnSettingsChanged(object? sender, AppSettingsChangedEventArgs e)
    {
        if (e.Previous.CacheLimitBytes != e.Current.CacheLimitBytes)
        {
            _publish(CacheInvalidation.ForGlobal(CacheInvalidationAspect.PhysicalSummary));
        }

        if (AffectsCoverage(e.Previous, e.Current))
        {
            PublishCoverageInvalidation();
        }
    }

    private void OnBookChanged(object? sender, BookCommittedChange change) =>
        _publish(CacheInvalidation.ForBook(change.BookId,
            change is BookCommittedChange.MetadataCommitted
                ? CacheInvalidationAspect.CatalogStructure
                : CacheInvalidationAspect.PhysicalSummary | CacheInvalidationAspect.CatalogStructure | CacheInvalidationAspect.Coverage));

    private void OnRegexRulesChanged(object? sender, RegexReplacementRulesChangedEventArgs e)
    {
        if (e.AffectsSpeechProfile)
        {
            PublishCoverageInvalidation();
        }
    }

    private void PublishCoverageInvalidation() =>
        _publish(CacheInvalidation.ForGlobal(CacheInvalidationAspect.Coverage));

    private static bool AffectsCoverage(AppSettings previous, AppSettings current) =>
        previous.DefaultSpeakSpeed != current.DefaultSpeakSpeed ||
        previous.ReadChapterTitle != current.ReadChapterTitle ||
        previous.EnableLongParagraphSplitting != current.EnableLongParagraphSplitting ||
        previous.LongParagraphThreshold != current.LongParagraphThreshold;
}
