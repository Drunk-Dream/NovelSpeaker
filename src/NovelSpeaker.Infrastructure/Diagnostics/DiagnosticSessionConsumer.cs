using NovelSpeaker.Application.Observability;

namespace NovelSpeaker.Infrastructure.Diagnostics;

/// <summary>
/// Registers the diagnostic session owner in the observability fan-out without creating a second owner.
/// </summary>
internal sealed class DiagnosticSessionConsumer(SqliteDiagnosticSessionStore store) : IObservabilityConsumer
{
    public void OnOperationStarted(OperationStarted operation) => store.OnOperationStarted(operation);

    public void OnOperationCompleted(OperationCompleted operation) => store.OnOperationCompleted(operation);

    public void OnDiagnosticEvent(DiagnosticEvent diagnosticEvent) => store.OnDiagnosticEvent(diagnosticEvent);
}
