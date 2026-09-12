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
        var markerSource = new DiagnosticFieldDefinition(
            "source",
            DiagnosticFieldType.Enum,
            DiagnosticPrivacyClass.LowCardinality,
            "The stable source of the marker.",
            ["user"]);
        var activeActivityCount = new DiagnosticFieldDefinition(
            "activeActivityCount",
            DiagnosticFieldType.Integer,
            DiagnosticPrivacyClass.Technical,
            "Number of active stable operations at the snapshot time.");
        var stoppedReason = new DiagnosticFieldDefinition(
            "reason",
            DiagnosticFieldType.Enum,
            DiagnosticPrivacyClass.LowCardinality,
            "Why collection stopped.",
            ["hard-cap", "storage-failure"]);

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
                OperationCatalog.TtsRetry),
            new DiagnosticDefinition(
                new DiagnosticDefinitionId("diagnostics.problem_marker"),
                DiagnosticDefinitionKind.Event,
                "diagnostics",
                "The user marked the approximate time of a problem.",
                [markerSource]),
            new DiagnosticDefinition(
                new DiagnosticDefinitionId("diagnostics.active_activities"),
                DiagnosticDefinitionKind.Snapshot,
                "diagnostics",
                "A bounded summary of active stable operations.",
                [activeActivityCount]),
            new DiagnosticDefinition(
                new DiagnosticDefinitionId("diagnostics.capture_stopped"),
                DiagnosticDefinitionKind.Event,
                "diagnostics",
                "Diagnostic collection stopped before the session ended.",
                [stoppedReason])
        ]);
    }
}
