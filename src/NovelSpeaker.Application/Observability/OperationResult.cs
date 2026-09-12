namespace NovelSpeaker.Application.Observability;

/// <summary>
/// A stable, privacy-safe operation result.
/// </summary>
public readonly record struct OperationResult
{
    private OperationResult(OperationOutcome outcome, string? failureCode)
    {
        if (outcome == OperationOutcome.Failed && string.IsNullOrWhiteSpace(failureCode))
        {
            throw new ArgumentException("Failed operations require a stable failure code.", nameof(failureCode));
        }

        if (outcome != OperationOutcome.Failed && failureCode is not null)
        {
            throw new ArgumentException("Only failed operations may carry a failure code.", nameof(failureCode));
        }

        Outcome = outcome;
        FailureCode = failureCode;
    }

    public OperationOutcome Outcome { get; }

    public string? FailureCode { get; }

    public static OperationResult Succeeded() => new(OperationOutcome.Succeeded, null);

    public static OperationResult Failed(string failureCode) =>
        new(OperationOutcome.Failed, NormalizeFailureCode(failureCode));

    public static OperationResult Cancelled() => new(OperationOutcome.Cancelled, null);

    private static string NormalizeFailureCode(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Any(character => !((character is >= 'a' and <= 'z') || char.IsAsciiDigit(character) || character is '.' or '-' or '_')))
        {
            throw new ArgumentException("Failure codes must use stable lower-case tokens.", nameof(value));
        }

        return value;
    }
}
