using Microsoft.Extensions.Logging;
using NovelSpeaker.Application.Observability;

namespace NovelSpeaker.Infrastructure.Diagnostics;

/// <summary>
/// Defines one stable production-log event.
/// </summary>
public sealed record LogEventDefinition
{
    public LogEventDefinition(
        int id,
        string eventName,
        string category,
        OperationDefinition? operation,
        LogLevel defaultLevel,
        string description)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(id, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        Id = id;
        EventName = eventName;
        Category = category;
        Operation = operation;
        DefaultLevel = defaultLevel;
        Description = description;
    }

    public int Id { get; }

    public string EventName { get; }

    public string Category { get; }

    public OperationDefinition? Operation { get; }

    public LogLevel DefaultLevel { get; }

    public string Description { get; }

    public EventId EventId => new(Id, EventName);
}
