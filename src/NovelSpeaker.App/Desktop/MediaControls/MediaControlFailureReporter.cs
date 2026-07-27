using Microsoft.Extensions.Logging;
using NovelSpeaker.Application.Desktop.MediaControls;

namespace NovelSpeaker.App.Desktop.MediaControls;

internal sealed class MediaControlFailureReporter : IMediaControlFailureReporter
{
    private readonly ILogger<MediaControlFailureReporter> _logger;

    public MediaControlFailureReporter(ILogger<MediaControlFailureReporter> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void ReportCommandFailure(MediaControlCommand command, Exception exception)
    {
        _logger.LogWarning(
            "System media command {Command} failed with {FailureType}.",
            command,
            exception.GetType().Name);
    }

    public void ReportMetadataFailure(Exception exception)
    {
        _logger.LogWarning(
            "Updating system media metadata failed with {FailureType}.",
            exception.GetType().Name);
    }
}
