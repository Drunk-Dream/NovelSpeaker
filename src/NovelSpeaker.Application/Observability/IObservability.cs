namespace NovelSpeaker.Application.Observability;

/// <summary>
/// Thin application-facing observability API. Storage is intentionally absent from this contract.
/// </summary>
public interface IObservability
{
    bool IsEnabled { get; }

    CorrelationContext CurrentContext { get; }

    IOperationScope StartOperation(OperationDefinition operation);

    void Record(DiagnosticDefinition definition, DiagnosticFieldSet fields);
}
