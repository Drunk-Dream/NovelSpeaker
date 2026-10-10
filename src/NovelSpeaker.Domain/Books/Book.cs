namespace NovelSpeaker.Domain.Books;

/// <summary>
/// A technical book key with immutable product identity and book-level current state.
/// </summary>
public sealed record Book
{
    public Book(
        string id,
        string title,
        string? author,
        string? activeSourceBindingId,
        DateTimeOffset importedAt,
        DateTimeOffset? lastPlayedAt,
        DateTimeOffset updatedAt,
        string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var identity = BookIdentity.Create(title, author);
        Id = id;
        Title = title;
        Author = author ?? "";
        NormalizedTitle = identity.NormalizedTitle;
        NormalizedAuthor = identity.NormalizedAuthor;
        ActiveSourceBindingId = activeSourceBindingId;
        ImportedAt = importedAt;
        LastPlayedAt = lastPlayedAt;
        UpdatedAt = updatedAt;
        Description = description;
    }

    public string Id { get; }
    public string Title { get; }
    public string Author { get; }
    public string NormalizedTitle { get; }
    public string NormalizedAuthor { get; }
    public string? ActiveSourceBindingId { get; init; }
    public DateTimeOffset ImportedAt { get; }
    public DateTimeOffset? LastPlayedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public string? Description { get; init; }
}
