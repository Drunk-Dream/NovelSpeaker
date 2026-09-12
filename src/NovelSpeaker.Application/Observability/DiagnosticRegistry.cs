namespace NovelSpeaker.Application.Observability;

/// <summary>
/// Enumerates and validates the stable diagnostic definition catalog.
/// </summary>
public sealed class DiagnosticRegistry
{
    private readonly IReadOnlyDictionary<DiagnosticDefinitionId, DiagnosticDefinition> _definitions;

    public DiagnosticRegistry(IEnumerable<DiagnosticDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        var values = definitions.ToArray();
        if (values.Any(definition => definition is null))
        {
            throw new ArgumentException("Diagnostic definitions cannot be null.", nameof(definitions));
        }

        if (values.GroupBy(definition => definition.Id).Any(group => group.Count() > 1))
        {
            throw new ArgumentException("Diagnostic definition ids must be unique.", nameof(definitions));
        }

        _definitions = values.ToDictionary(definition => definition.Id);
    }

    public static DiagnosticRegistry Default { get; } = CreateDefault();

    public IReadOnlyList<DiagnosticDefinition> Definitions =>
        _definitions.Values.OrderBy(definition => definition.Id.Value, StringComparer.Ordinal).ToArray();

    public DiagnosticDefinition Get(DiagnosticDefinitionId id) =>
        _definitions.TryGetValue(id, out var definition)
            ? definition
            : throw new KeyNotFoundException($"Diagnostic definition '{id}' is not registered.");

    private static DiagnosticRegistry CreateDefault()
    {
        var lifecyclePhase = new DiagnosticFieldDefinition(
            "phase",
            DiagnosticFieldType.Enum,
            DiagnosticPrivacyClass.LowCardinality,
            "Stable lifecycle phase.",
            ["startup", "shutdown"]);
        var outcome = new DiagnosticFieldDefinition(
            "outcome",
            DiagnosticFieldType.Enum,
            DiagnosticPrivacyClass.LowCardinality,
            "Stable operation outcome.",
            ["succeeded", "failed", "cancelled"]);
        var retryCount = new DiagnosticFieldDefinition(
            "retryCount",
            DiagnosticFieldType.Integer,
            DiagnosticPrivacyClass.Technical,
            "Number of retries already attempted.");

        return new DiagnosticRegistry(
        [
            new DiagnosticDefinition(
                new DiagnosticDefinitionId("app.lifecycle"),
                DiagnosticDefinitionKind.Event,
                "application",
                "A stable application lifecycle transition.",
                [lifecyclePhase],
                null),
            new DiagnosticDefinition(
                new DiagnosticDefinitionId("operation.completed"),
                DiagnosticDefinitionKind.Event,
                "application",
                "A stable operation completed.",
                [outcome],
                null),
            new DiagnosticDefinition(
                new DiagnosticDefinitionId("tts.retry"),
                DiagnosticDefinitionKind.Event,
                "speech",
                "A text-to-speech operation was retried.",
                [retryCount],
                OperationCatalog.TtsRetry)
        ]);
    }
}
