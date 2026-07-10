using NovelSpeaker.Domain.Books;

namespace NovelSpeaker.Application.Books;

/// <summary>Editable fields of a regex replacement rule.</summary>
public sealed record RegexReplacementRuleEditorModel(
    Guid? Id,
    string Name,
    string Pattern,
    string Replacement,
    RegexReplacementScope Scope);
