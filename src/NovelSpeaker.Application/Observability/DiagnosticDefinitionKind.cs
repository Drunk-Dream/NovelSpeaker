namespace NovelSpeaker.Application.Observability;

/// <summary>
/// The diagnostic record shape represented by a registry definition.
/// </summary>
public enum DiagnosticDefinitionKind
{
    Activity,
    Event,
    Snapshot
}
