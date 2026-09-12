using NovelSpeaker.Application.Observability;

namespace NovelSpeaker.Infrastructure.Diagnostics;

internal sealed class PerformanceTelemetryConsumer(LocalPerformanceTelemetryStore store) : IObservabilityConsumer
{
    public void OnOperationStarted(OperationStarted operation) => store.OnOperationStarted(operation);

    public void OnOperationCompleted(OperationCompleted operation) => store.OnOperationCompleted(operation);

    public void OnDiagnosticEvent(DiagnosticEvent diagnosticEvent) => store.OnDiagnosticEvent(diagnosticEvent);
}
