using Microsoft.Extensions.Logging;
using NovelSpeaker.Application.Playback;

namespace NovelSpeaker.Infrastructure.Playback;

/// <summary>
/// Records chapter read failures using only stable operation and exception-type details.
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
        _logger.LogWarning(
            "Chapter content read unavailable. ExceptionType={ExceptionType}",
            exception.GetType().Name);
    }
}
