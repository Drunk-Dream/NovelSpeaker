namespace NovelSpeaker.Application.Observability;

/// <summary>
/// The small set of outcomes that can be attached to an operation.
/// </summary>
public enum OperationOutcome
{
    Succeeded,
    Failed,
    Cancelled
}
