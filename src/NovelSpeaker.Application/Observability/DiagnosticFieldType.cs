namespace NovelSpeaker.Application.Observability;

/// <summary>
/// The allowed primitive shapes for a structured diagnostic field.
/// </summary>
public enum DiagnosticFieldType
{
    Boolean,
    Integer,
    Decimal,
    DurationMilliseconds,
    Enum,
    String
}
