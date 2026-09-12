namespace NovelSpeaker.Application.Observability;

/// <summary>
/// A timestamped structured diagnostic record delivered to consumers.
/// </summary>
public sealed record DiagnosticEvent
{
    public DiagnosticEvent(
        DiagnosticDefinition definition,
        DiagnosticFieldSet fields,
        CorrelationContext context,
        DateTimeOffset timestampUtc)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(context);
        if (!ReferenceEquals(definition, fields.Definition))
        {
            throw new ArgumentException("Diagnostic fields must belong to the supplied definition.", nameof(fields));
        }

        Definition = definition;
        Fields = fields;
        Context = context;
        TimestampUtc = timestampUtc.ToUniversalTime();
    }

    public DiagnosticDefinition Definition { get; }

    public DiagnosticFieldSet Fields { get; }

    public CorrelationContext Context { get; }

    public DateTimeOffset TimestampUtc { get; }
}
