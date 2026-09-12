using Microsoft.Extensions.Logging;

namespace NovelSpeaker.Infrastructure.Diagnostics;

/// <summary>
/// Adapts pre-DI startup lifecycle messages to the shared production logger.
/// </summary>
public sealed class StartupDiagnosticsRecorder
{
    private readonly ILogger _logger;

    public StartupDiagnosticsRecorder(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void RecordStage(string stage, string message)
    {
        _logger.LogInformation(
            LogEventRegistry.StartupStage.EventId,
            "{Stage}: {Detail}",
            stage,
            message);
    }

    public void RecordFailure(string stage, string message, Exception? exception)
    {
        _logger.Log(
            LogLevel.Error,
            LogEventRegistry.StartupFailure.EventId,
            exception,
            "{Stage}: {Detail}",
            stage,
            message);
    }
}
