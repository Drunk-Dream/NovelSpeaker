namespace NovelSpeaker.Application.Observability;

/// <summary>
/// Immutable operation-completion notification delivered to observability consumers.
/// </summary>
public sealed record OperationCompleted(
    OperationDefinition Operation,
    CorrelationContext Context,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    OperationResult Result)
{
    public TimeSpan Duration => CompletedAtUtc - StartedAtUtc;
}
