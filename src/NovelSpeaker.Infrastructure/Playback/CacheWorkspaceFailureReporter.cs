using Microsoft.Extensions.Logging;
using NovelSpeaker.Application.Playback.Cache;

namespace NovelSpeaker.Infrastructure.Playback;

/// <summary>
/// Records cache estimation fallbacks using only a stable operation and exception type.
/// </summary>
public sealed class CacheWorkspaceFailureReporter : ICacheWorkspaceFailureReporter
{
    private readonly ILogger<CacheWorkspaceFailureReporter> _logger;

    public CacheWorkspaceFailureReporter(ILogger<CacheWorkspaceFailureReporter> logger)
    {
        _logger = logger;
    }

    public void ReportEstimationFallback(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        _logger.LogWarning(
            "Cache chapter completeness estimate unavailable; falling back to unknown. ExceptionType={ExceptionType}",
            exception.GetType().Name);
    }
}
