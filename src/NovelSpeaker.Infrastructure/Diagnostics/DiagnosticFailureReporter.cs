using Microsoft.Extensions.Logging;
using NovelSpeaker.Application.Diagnostics;
using System.Collections;

namespace NovelSpeaker.Infrastructure.Diagnostics;

internal sealed class DiagnosticFailureReporter : IDiagnosticFailureReporter
{
    private readonly ILogger<DiagnosticFailureReporter> _logger;

    public DiagnosticFailureReporter(ILogger<DiagnosticFailureReporter> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void ReportFailure(
        DiagnosticFailureOperation operation,
        DiagnosticFailureStage stage,
        Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        try
        {
            var operationName = GetOperationName(operation);
            var stageName = GetStageName(stage);
            var state = new DiagnosticFailureLogState(
                $"Diagnostic operation '{operationName}' failed at '{stageName}'.",
                operationName,
                stageName,
                SanitizedExceptionDetails.CreateTypeChain(exception));
            _logger.Log(
                LogLevel.Error,
                LogEventRegistry.DiagnosticsOperationFailed.EventId,
                state,
                exception: null,
                static (current, _) => current.Message);
        }
        catch
        {
            // Failure reporting must never change the diagnostic operation's result.
        }
    }

    private static string GetOperationName(DiagnosticFailureOperation operation) => operation switch
    {
        DiagnosticFailureOperation.DiagnosticsExport => "diagnostics-export",
        DiagnosticFailureOperation.ProblemDiagnosticsExport => "problem-diagnostics-export",
        DiagnosticFailureOperation.SessionStart => "session-start",
        DiagnosticFailureOperation.SessionWrite => "session-write",
        DiagnosticFailureOperation.SessionRecover => "session-recover",
        DiagnosticFailureOperation.SessionEnd => "session-end",
        DiagnosticFailureOperation.ProblemMarker => "problem-marker",
        DiagnosticFailureOperation.WindowCapture => "window-capture",
        _ => "unknown"
    };

    private static string GetStageName(DiagnosticFailureStage stage) => stage switch
    {
        DiagnosticFailureStage.PrepareBundle => "prepare-bundle",
        DiagnosticFailureStage.ReadTelemetry => "read-telemetry",
        DiagnosticFailureStage.ReadLogs => "read-logs",
        DiagnosticFailureStage.ReadSession => "read-session",
        DiagnosticFailureStage.BuildBundle => "build-bundle",
        DiagnosticFailureStage.CommitBundle => "commit-bundle",
        DiagnosticFailureStage.CreateSession => "create-session",
        DiagnosticFailureStage.WriteSession => "write-session",
        DiagnosticFailureStage.ReadSessionMarker => "read-session-marker",
        DiagnosticFailureStage.RecoverSession => "recover-session",
        DiagnosticFailureStage.EndSession => "end-session",
        DiagnosticFailureStage.RecordMarker => "record-marker",
        DiagnosticFailureStage.CaptureWindow => "capture-window",
        DiagnosticFailureStage.SaveAttachment => "save-attachment",
        _ => "unknown"
    };

    private sealed class DiagnosticFailureLogState : IReadOnlyList<KeyValuePair<string, object?>>, IStructuredExceptionLogState
    {
        private readonly KeyValuePair<string, object?>[] _properties;

        public DiagnosticFailureLogState(
            string message,
            string diagnosticOperation,
            string stage,
            SanitizedExceptionDetails exceptionDetails)
        {
            Message = message;
            ExceptionDetails = exceptionDetails;
            _properties =
            [
                new KeyValuePair<string, object?>("Message", message),
                new KeyValuePair<string, object?>("DiagnosticOperation", diagnosticOperation),
                new KeyValuePair<string, object?>("Stage", stage),
                new KeyValuePair<string, object?>("{OriginalFormat}", "Diagnostic operation failed.")
            ];
        }

        public string Message { get; }

        public SanitizedExceptionDetails ExceptionDetails { get; }

        public int Count => _properties.Length;

        public KeyValuePair<string, object?> this[int index] => _properties[index];

        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() =>
            ((IEnumerable<KeyValuePair<string, object?>>)_properties).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => _properties.GetEnumerator();
    }
}
