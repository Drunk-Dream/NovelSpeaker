using System.Collections.Concurrent;
using NovelSpeaker.Application.Books;

namespace NovelSpeaker.Infrastructure.Books;

/// <summary>Thread-safe in-memory projection of safe regex execution errors for the workspace.</summary>
public sealed class RegexReplacementRuleErrorStore : IRegexReplacementRuleErrorStore
{
    private readonly ConcurrentDictionary<Guid, string> _errors = [];

    public IReadOnlyDictionary<Guid, string> Current => _errors;

    public void Replace(IReadOnlyDictionary<Guid, string> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        _errors.Clear();
        foreach (var (ruleId, message) in errors)
        {
            _errors[ruleId] = message;
        }
    }
}
