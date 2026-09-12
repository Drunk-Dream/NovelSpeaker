namespace NovelSpeaker.Application.Observability;

/// <summary>
/// Immutable operation-start notification delivered to observability consumers.
/// </summary>
public sealed record OperationStarted(
    OperationDefinition Operation,
    CorrelationContext Context,
    DateTimeOffset TimestampUtc);
