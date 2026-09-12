namespace NovelSpeaker.Application.Observability;

/// <summary>
/// Provides the current process and scoped diagnostic correlation without exposing storage ids.
/// </summary>
public interface IObservabilityContextAccessor
{
    CorrelationContext Current { get; }

    IDisposable Push(CorrelationContext context);

    void SetDiagnosticSession(string? diagnosticSessionId);
}
