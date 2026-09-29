using Microsoft.Extensions.Logging;
using NovelSpeaker.Application.Diagnostics;
using NovelSpeaker.Application.Observability;

namespace NovelSpeaker.Infrastructure.Diagnostics;

/// <summary>
/// Adapts process facts to independent, best-effort diagnostic sinks, including pre-DI startup.
/// </summary>
public sealed class ProcessDiagnosticsRecorder
{
    private readonly ILogger _logger;

    public ProcessDiagnosticsRecorder(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void RecordStartupStage(string stage, string message)
    {
        TryRecord(() => _logger.LogInformation(
            LogEventRegistry.StartupStage.EventId,
            "{Stage}: {Detail}",
            stage,
            message));
    }

    public void RecordStartupFailure(string stage, string message, Exception? exception)
    {
        TryRecord(() => _logger.Log(
            LogLevel.Error,
            LogEventRegistry.StartupFailure.EventId,
            exception,
            "{Stage}: {Detail}",
            stage,
            message));
    }

    public void RecordLifecycleFailure(string source, string message, Exception? exception)
    {
        TryRecord(() => _logger.LogError(
            LogEventRegistry.LifecycleFailure.EventId,
            exception,
            "{Source}: {Detail}", source, message));
    }

    public void RecordLifecycleStage(string source, string message)
    {
        TryRecord(() => _logger.LogInformation("{Source}: {Detail}", source, message));
    }

    public void RecordProcessFailure(
        ProcessFailure failure,
        string source,
        string message,
        Exception? exception,
        IObservability? observability)
    {
        TryRecord(() => _logger.Log(
            failure.IsFatal ? LogLevel.Critical : LogLevel.Error,
            failure.ExitReason == ProcessExitReason.StartupFailure
                ? LogEventRegistry.StartupFailure.EventId
                : LogEventRegistry.RuntimeFailure.EventId,
            exception,
            "{Source}: {Detail}; {FailureSource}, {Severity}, {Action}",
            source, message, failure.Source, failure.Severity, failure.Action));

        TryRecord(() =>
        {
            var definition = DiagnosticRegistry.Default.Get(new DiagnosticDefinitionId("app.process.failure"));
            observability?.Record(definition, DiagnosticFieldSet.Create(definition,
            [
                DiagnosticFieldValue.Enum(definition.Fields[0], failure.Source),
                DiagnosticFieldValue.Enum(definition.Fields[1], failure.Severity),
                DiagnosticFieldValue.Enum(definition.Fields[2], failure.Action)
            ]));
        });
    }

    public void RecordProcessExit(ProcessExitReason exitReason)
    {
        TryRecord(() => _logger.LogInformation(
            LogEventRegistry.ProcessExit.EventId,
            "Process ended: {ExitReason}", exitReason.Value));
    }

    private static void TryRecord(Action record)
    {
        try
        {
            record();
        }
        catch
        {
            // Each sink is independent: failure must neither suppress another sink nor prevent shutdown.
        }
    }
}
