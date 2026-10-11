namespace NovelSpeaker.Domain.Books;

/// <summary>
/// Binds one book to a concrete content source; this is not a global source definition.
/// </summary>
public sealed record BookSourceBinding(
    string BindingId,
    string BookId,
    SourceType SourceType,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
