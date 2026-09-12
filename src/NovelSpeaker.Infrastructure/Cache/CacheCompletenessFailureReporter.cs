using Microsoft.Extensions.Logging;
using NovelSpeaker.Application.Cache;
using NovelSpeaker.Infrastructure.Diagnostics;
using NovelSpeaker.Infrastructure.Speech;

namespace NovelSpeaker.Infrastructure.Cache;

/// <summary>
/// Records unavailable cache-completeness results with stable event semantics and redacted exception details.
/// </summary>
public sealed class CacheCompletenessFailureReporter : ICacheCompletenessFailureReporter
{
    private readonly ILogger<CacheCompletenessFailureReporter> _logger;

    public CacheCompletenessFailureReporter(ILogger<CacheCompletenessFailureReporter> logger)
    {
        _logger = logger;
    }

    public void ReportCompletenessUnavailable(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        SensitiveFailureLogger.LogWarning(
            _logger,
            LogEventRegistry.CacheCompletenessUnavailable,
            exception,
            []);
    }
}
