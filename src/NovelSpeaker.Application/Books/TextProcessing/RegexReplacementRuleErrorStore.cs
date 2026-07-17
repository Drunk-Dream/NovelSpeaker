using System.Collections.Concurrent;

namespace NovelSpeaker.Application.Books.TextProcessing;

/// <summary>Owns the process-local projection of safe regex execution errors.</summary>
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
