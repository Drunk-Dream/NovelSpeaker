using Microsoft.Extensions.Logging;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Infrastructure.Diagnostics;
using NovelSpeaker.Infrastructure.Speech;

namespace NovelSpeaker.Infrastructure.Playback;

/// <summary>
/// Records chapter read failures with stable event semantics and redacted exception details.
/// </summary>
public sealed class BookPlaybackContentFailureReporter : IBookPlaybackContentFailureReporter
{
    private readonly ILogger<BookPlaybackContentFailureReporter> _logger;

    public BookPlaybackContentFailureReporter(ILogger<BookPlaybackContentFailureReporter> logger)
    {
        _logger = logger;
    }

    public void ReportChapterReadFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        SensitiveFailureLogger.LogWarning(
            _logger,
            LogEventRegistry.PlaybackContentUnavailable,
            exception,
            []);
    }
}
