namespace NovelSpeaker.Application.Observability;

/// <summary>
/// Describes a stable operation vocabulary entry.
/// </summary>
public sealed record OperationDefinition
{
    public OperationDefinition(OperationId id, string name, string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id.Value);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        Id = id;
        Name = name;
        Description = description;
    }

    public OperationId Id { get; }

    public string Name { get; }

    public string Description { get; }
}
