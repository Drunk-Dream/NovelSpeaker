namespace NovelSpeaker.Application.Observability;

/// <summary>
/// A sparse set of values that can only contain fields declared by one definition.
/// </summary>
public sealed class DiagnosticFieldSet
{
    private DiagnosticFieldSet(DiagnosticDefinition definition, IReadOnlyList<DiagnosticFieldValue> values)
    {
        Definition = definition;
        Values = Array.AsReadOnly(values.ToArray());
    }

    public DiagnosticDefinition Definition { get; }

    public IReadOnlyList<DiagnosticFieldValue> Values { get; }

    public static DiagnosticFieldSet Create(
        DiagnosticDefinition definition,
        IEnumerable<DiagnosticFieldValue>? values = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var suppliedValues = (values ?? []).ToArray();
        var declaredFields = definition.Fields.ToHashSet(ReferenceEqualityComparer.Instance);
        if (suppliedValues.Any(value => !declaredFields.Contains(value.Field)))
        {
            throw new ArgumentException("Diagnostic values must use fields declared by the definition.", nameof(values));
        }

        if (suppliedValues.Select(value => value.Field).Distinct(ReferenceEqualityComparer.Instance).Count() != suppliedValues.Length)
        {
            throw new ArgumentException("A diagnostic field may only be supplied once.", nameof(values));
        }

        return new DiagnosticFieldSet(definition, suppliedValues);
    }
}
