namespace NovelSpeaker.Application.Observability;

/// <summary>
/// Privacy classification declared by a diagnostic field definition.
/// </summary>
public enum DiagnosticPrivacyClass
{
    Technical,
    LowCardinality,
    UserContent,
    Sensitive
}
