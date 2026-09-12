namespace NovelSpeaker.Application.Observability;

/// <summary>
/// A versioned, privacy-reviewed activity, event, or snapshot contract.
/// </summary>
public sealed class DiagnosticDefinition
{
    public DiagnosticDefinition(
        DiagnosticDefinitionId id,
        DiagnosticDefinitionKind kind,
        string domain,
        string description,
        IEnumerable<DiagnosticFieldDefinition> fields,
        OperationDefinition? operation = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentNullException.ThrowIfNull(fields);

        var declaredFields = fields.ToArray();
        if (declaredFields.Any(field => field is null))
        {
            throw new ArgumentException("Diagnostic fields cannot be null.", nameof(fields));
        }

        if (declaredFields.GroupBy(field => field.Name, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
        {
            throw new ArgumentException("Diagnostic field names must be unique within a definition.", nameof(fields));
        }

        foreach (var field in declaredFields)
        {
            ArgumentNullException.ThrowIfNull(field);
            if (field.Privacy is DiagnosticPrivacyClass.UserContent or DiagnosticPrivacyClass.Sensitive ||
                IsForbiddenFieldName(field.Name))
            {
                throw new ArgumentException(
                    $"Diagnostic field '{field.Name}' is not allowed in structured diagnostics.",
                    nameof(fields));
            }
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(id.Value);
        Id = id;
        Kind = kind;
        Domain = domain;
        Description = description;
        Fields = Array.AsReadOnly(declaredFields);
        Operation = operation;
    }

    public DiagnosticDefinitionId Id { get; }

    public DiagnosticDefinitionKind Kind { get; }

    public string Domain { get; }

    public string Description { get; }

    public IReadOnlyList<DiagnosticFieldDefinition> Fields { get; }

    public OperationDefinition? Operation { get; }

    public DiagnosticFieldSet CreateFields(params DiagnosticFieldValue[] values) =>
        DiagnosticFieldSet.Create(this, values);

    private static bool IsForbiddenFieldName(string name)
    {
        var normalized = name.Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(".", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();

        return normalized.Contains("booktitle", StringComparison.Ordinal) ||
               normalized.Contains("chaptertitle", StringComparison.Ordinal) ||
               normalized.Contains("bookname", StringComparison.Ordinal) ||
               normalized.Contains("chaptername", StringComparison.Ordinal) ||
               normalized.Contains("bookid", StringComparison.Ordinal) ||
               normalized.Contains("chapterid", StringComparison.Ordinal) ||
               normalized.Contains("content", StringComparison.Ordinal) ||
               normalized.Contains("usertext", StringComparison.Ordinal) ||
               normalized.Contains("filepath", StringComparison.Ordinal) ||
               normalized.Equals("path", StringComparison.Ordinal) ||
               normalized.Equals("url", StringComparison.Ordinal) ||
               normalized.Contains("query", StringComparison.Ordinal) ||
               normalized.Contains("header", StringComparison.Ordinal) ||
               normalized.Contains("body", StringComparison.Ordinal) ||
               normalized.Contains("token", StringComparison.Ordinal) ||
               normalized.Contains("apikey", StringComparison.Ordinal) ||
               normalized.Contains("regex", StringComparison.Ordinal) ||
               normalized.Contains("sql", StringComparison.Ordinal) ||
               normalized.Equals("text", StringComparison.Ordinal);
    }
}
