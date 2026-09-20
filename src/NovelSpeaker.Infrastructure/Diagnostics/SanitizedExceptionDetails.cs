namespace NovelSpeaker.Infrastructure.Diagnostics;

internal interface IStructuredExceptionLogState
{
    SanitizedExceptionDetails ExceptionDetails { get; }
}

internal sealed record SanitizedExceptionDetails(
    string Type,
    int HResult,
    string? Message,
    string? StackTrace,
    IReadOnlyList<SanitizedExceptionDetails> Children,
    bool Truncated)
{
    private const int MaxDepth = 8;
    private const int MaxChildren = 16;
    private const int MaxNodes = 64;

    public static SanitizedExceptionDetails Create(
        Exception exception,
        IEnumerable<string?>? knownSecrets = null)
        => Create(exception, knownSecrets, includeMessagesAndStackTraces: true);

    public static SanitizedExceptionDetails CreateTypeChain(Exception exception)
        => Create(exception, knownSecrets: null, includeMessagesAndStackTraces: false);

    private static SanitizedExceptionDetails Create(
        Exception exception,
        IEnumerable<string?>? knownSecrets,
        bool includeMessagesAndStackTraces)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var secrets = knownSecrets is null
            ? Array.Empty<string>()
            : knownSecrets
                .OfType<string>()
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        return Create(exception, secrets, 0, new ExceptionBudget(MaxNodes - 1), includeMessagesAndStackTraces);
    }

    private static SanitizedExceptionDetails Create(
        Exception exception,
        IReadOnlyList<string> knownSecrets,
        int depth,
        ExceptionBudget budget,
        bool includeMessagesAndStackTraces)
    {
        var candidateChildren = exception is AggregateException aggregate
            ? aggregate.InnerExceptions
            : exception.InnerException is null
                ? []
                : [exception.InnerException];
        var children = new List<SanitizedExceptionDetails>(Math.Min(candidateChildren.Count, MaxChildren));
        var truncated = depth >= MaxDepth && candidateChildren.Count > 0;
        if (!truncated)
        {
            foreach (var child in candidateChildren.Take(MaxChildren))
            {
                if (!budget.TryTake())
                {
                    truncated = true;
                    break;
                }

                children.Add(Create(child, knownSecrets, depth + 1, budget, includeMessagesAndStackTraces));
            }

            truncated |= candidateChildren.Count > MaxChildren;
        }

        return new SanitizedExceptionDetails(
            exception.GetType().FullName ?? exception.GetType().Name,
            exception.HResult,
            includeMessagesAndStackTraces ? LogTextSanitizer.Sanitize(exception.Message, knownSecrets) : null,
            !includeMessagesAndStackTraces || exception.StackTrace is null
                ? null
                : LogTextSanitizer.Sanitize(exception.StackTrace, knownSecrets),
            children,
            truncated);
    }

    private sealed class ExceptionBudget(int remaining)
    {
        public bool TryTake()
        {
            if (remaining <= 0)
            {
                return false;
            }

            remaining--;
            return true;
        }
    }
}
