using Microsoft.Extensions.Logging;
using NovelSpeaker.Application.Playback.Cache;

namespace NovelSpeaker.Infrastructure.Playback;

/// <summary>
/// Records unavailable cache-completeness results using only a stable operation and exception type.
/// </summary>
public sealed class CacheWorkspaceFailureReporter : ICacheWorkspaceFailureReporter
{
    private readonly ILogger<CacheWorkspaceFailureReporter> _logger;

    public CacheWorkspaceFailureReporter(ILogger<CacheWorkspaceFailureReporter> logger)
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
