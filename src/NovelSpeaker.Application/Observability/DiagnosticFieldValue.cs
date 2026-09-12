namespace NovelSpeaker.Application.Observability;

/// <summary>
/// A typed value bound to a declared diagnostic field.
/// </summary>
public readonly record struct DiagnosticFieldValue
{
    private readonly bool _booleanValue;
    private readonly long _integerValue;
    private readonly double _decimalValue;
    private readonly string? _stringValue;

    private DiagnosticFieldValue(
        DiagnosticFieldDefinition field,
        DiagnosticFieldType type,
        bool booleanValue = false,
        long integerValue = 0,
        double decimalValue = 0,
        string? stringValue = null)
    {
        ArgumentNullException.ThrowIfNull(field);
        Field = field;
        Type = type;
        _booleanValue = booleanValue;
        _integerValue = integerValue;
        _decimalValue = decimalValue;
        _stringValue = stringValue;
    }

    public DiagnosticFieldDefinition Field { get; }

    public DiagnosticFieldType Type { get; }

    public bool BooleanValue => _booleanValue;

    public long IntegerValue => _integerValue;

    public double DecimalValue => _decimalValue;

    public string? StringValue => _stringValue;

    public static DiagnosticFieldValue Boolean(DiagnosticFieldDefinition field, bool value) =>
        For(field, DiagnosticFieldType.Boolean, booleanValue: value);

    public static DiagnosticFieldValue Integer(DiagnosticFieldDefinition field, long value) =>
        For(field, DiagnosticFieldType.Integer, integerValue: value);

    public static DiagnosticFieldValue Decimal(DiagnosticFieldDefinition field, double value) =>
        For(field, DiagnosticFieldType.Decimal, decimalValue: value);

    public static DiagnosticFieldValue DurationMilliseconds(DiagnosticFieldDefinition field, double value) =>
        For(field, DiagnosticFieldType.DurationMilliseconds, decimalValue: value);

    public static DiagnosticFieldValue Enum(DiagnosticFieldDefinition field, string value) =>
        For(field, DiagnosticFieldType.Enum, stringValue: value);

    public static DiagnosticFieldValue String(DiagnosticFieldDefinition field, string value) =>
        For(field, DiagnosticFieldType.String, stringValue: value);

    private static DiagnosticFieldValue For(
        DiagnosticFieldDefinition field,
        DiagnosticFieldType type,
        bool booleanValue = false,
        long integerValue = 0,
        double decimalValue = 0,
        string? stringValue = null)
    {
        ArgumentNullException.ThrowIfNull(field);
        if (field.Type != type)
        {
            throw new ArgumentException($"Field '{field.Name}' does not accept {type} values.", nameof(field));
        }

        if (type is DiagnosticFieldType.Decimal or DiagnosticFieldType.DurationMilliseconds &&
            (!double.IsFinite(decimalValue) || decimalValue < 0 && type == DiagnosticFieldType.DurationMilliseconds))
        {
            throw new ArgumentOutOfRangeException(nameof(decimalValue));
        }

        if (type is DiagnosticFieldType.Enum or DiagnosticFieldType.String)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(stringValue);
            if (!field.AllowedValues.Contains(stringValue, StringComparer.Ordinal))
            {
                throw new ArgumentException($"Value '{stringValue}' is not allowed for field '{field.Name}'.", nameof(stringValue));
            }
        }

        return new DiagnosticFieldValue(field, type, booleanValue, integerValue, decimalValue, stringValue);
    }
}
