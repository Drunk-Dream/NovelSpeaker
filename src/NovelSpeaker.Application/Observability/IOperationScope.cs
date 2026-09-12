namespace NovelSpeaker.Application.Observability;

/// <summary>
/// A scoped stable operation boundary.
/// </summary>
public interface IOperationScope : IDisposable
{
    OperationDefinition Operation { get; }

    CorrelationContext Context { get; }

    bool IsCompleted { get; }

    void Complete(OperationResult result);
}
