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
    private bool _disposed;

    public CacheConfigurationChangeObserver(
        IAppSettingsService settingsService,
        IRegexReplacementRuleWorkspaceService? regexWorkspace,
        Action<CacheInvalidation> publish)
    {
        _settingsService = settingsService;
        _regexWorkspace = regexWorkspace;
        _publish = publish;

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
        if (_regexWorkspace is not null)
        {
            _regexWorkspace.Changed -= OnRegexRulesChanged;
        }
    }

    private void OnSettingsChanged(object? sender, AppSettingsChangedEventArgs e)
    {
        if (AffectsCoverage(e.Previous, e.Current))
        {
            PublishCoverageInvalidation();
        }
    }

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
        previous.CurrentProviderId != current.CurrentProviderId ||
        previous.DefaultSpeakSpeed != current.DefaultSpeakSpeed ||
        previous.ReadChapterTitle != current.ReadChapterTitle ||
        previous.EnableLongParagraphSplitting != current.EnableLongParagraphSplitting ||
        previous.LongParagraphThreshold != current.LongParagraphThreshold;
}
