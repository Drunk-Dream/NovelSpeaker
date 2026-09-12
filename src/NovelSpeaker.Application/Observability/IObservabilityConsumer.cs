namespace NovelSpeaker.Application.Observability;

/// <summary>
/// Independent consumer of the thin observability contract.
/// </summary>
public interface IObservabilityConsumer
{
    void OnOperationStarted(OperationStarted operation);

    void OnOperationCompleted(OperationCompleted operation);

    void OnDiagnosticEvent(DiagnosticEvent diagnosticEvent);
}
