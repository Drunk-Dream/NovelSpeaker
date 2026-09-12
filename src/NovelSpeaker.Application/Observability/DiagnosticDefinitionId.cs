namespace NovelSpeaker.Application.Observability;

/// <summary>
/// Identifies a stable diagnostic definition.
/// </summary>
public readonly record struct DiagnosticDefinitionId
{
    public DiagnosticDefinitionId(string value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !(value[0] is >= 'a' and <= 'z') ||
            value.Any(character => !((character is >= 'a' and <= 'z') || char.IsAsciiDigit(character) || character is '.' or '-' or '_')) ||
            !char.IsAsciiLetterOrDigit(value[^1]))
        {
            throw new ArgumentException("Diagnostic definition ids must use lower-case stable tokens.", nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
