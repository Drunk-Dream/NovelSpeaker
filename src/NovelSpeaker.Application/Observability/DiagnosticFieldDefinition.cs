namespace NovelSpeaker.Application.Observability;

/// <summary>
/// Declares one privacy-reviewed field in a diagnostic definition.
/// </summary>
public sealed class DiagnosticFieldDefinition
{
    public DiagnosticFieldDefinition(
        string name,
        DiagnosticFieldType type,
        DiagnosticPrivacyClass privacy,
        string description,
        IEnumerable<string>? allowedValues = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        if (name.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_')) ||
            !char.IsAsciiLetter(name[0]))
        {
            throw new ArgumentException("Diagnostic field names must use stable tokens.", nameof(name));
        }

        var values = (allowedValues ?? []).ToArray();
        if (type is DiagnosticFieldType.Enum or DiagnosticFieldType.String && values.Length == 0)
        {
            throw new ArgumentException("String and enum fields must declare allowed values.", nameof(allowedValues));
        }

        if (type is not DiagnosticFieldType.Enum and not DiagnosticFieldType.String && values.Length > 0)
        {
            throw new ArgumentException("Only string and enum fields may declare allowed values.", nameof(allowedValues));
        }

        if (values.Any(value => string.IsNullOrWhiteSpace(value) ||
                               value.Any(character => !((character is >= 'a' and <= 'z') || char.IsAsciiDigit(character) || character is '.' or '-' or '_'))))
        {
            throw new ArgumentException("Allowed values must use stable lower-case tokens.", nameof(allowedValues));
        }

        Name = name;
        Type = type;
        Privacy = privacy;
        Description = description;
        AllowedValues = Array.AsReadOnly(values);
    }

    public string Name { get; }

    public DiagnosticFieldType Type { get; }

    public DiagnosticPrivacyClass Privacy { get; }

    public string Description { get; }

    public IReadOnlyList<string> AllowedValues { get; }
}
