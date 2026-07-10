namespace NovelSpeaker.Application.Books;

/// <summary>
/// Holds the latest safe runtime errors for regex rules. It is intentionally transient: errors
/// describe current execution health rather than user configuration and are never persisted.
/// </summary>
public interface IRegexReplacementRuleErrorStore
{
    IReadOnlyDictionary<Guid, string> Current { get; }

    void Replace(IReadOnlyDictionary<Guid, string> errors);
}
