namespace NovelSpeaker.Application.Observability;

/// <summary>
/// Identifies a stable user or system operation rather than an implementation method.
/// </summary>
public readonly record struct OperationId
{
    public OperationId(string value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Any(character => !((character is >= 'a' and <= 'z') || char.IsAsciiDigit(character) || character is '.' or '-' or '_')) ||
            !(value[0] is >= 'a' and <= 'z') ||
            !char.IsAsciiLetterOrDigit(value[^1]))
        {
            throw new ArgumentException("Operation ids must use lower-case stable tokens.", nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}
