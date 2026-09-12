namespace NovelSpeaker.Application.Observability;

/// <summary>
/// Correlation values that may be safely propagated to independent observability consumers.
/// </summary>
public sealed record CorrelationContext
{
    public CorrelationContext(string processInstanceId, string? diagnosticSessionId = null, string? activityId = null)
    {
        ProcessInstanceId = RequireToken(processInstanceId, nameof(processInstanceId));
        DiagnosticSessionId = ValidateOptionalToken(diagnosticSessionId, nameof(diagnosticSessionId));
        ActivityId = ValidateOptionalToken(activityId, nameof(activityId));
    }

    public string ProcessInstanceId { get; }

    public string? DiagnosticSessionId { get; }

    public string? ActivityId { get; }

    private static string RequireToken(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_')))
        {
            throw new ArgumentException("Correlation values must use opaque tokens.", parameterName);
        }

        return value;
    }

    private static string? ValidateOptionalToken(string? value, string parameterName) =>
        value is null ? null : RequireToken(value, parameterName);
}
