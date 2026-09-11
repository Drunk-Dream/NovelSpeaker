using Microsoft.Extensions.Logging;
using NovelSpeaker.Application.Cache;

namespace NovelSpeaker.Infrastructure.Cache;

/// <summary>
/// Records unavailable cache-completeness results using only a stable operation and exception type.
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
        _logger.LogWarning(
            "Cache chapter completeness unavailable for the current configuration. ExceptionType={ExceptionType}",
            exception.GetType().Name);
    }
}
