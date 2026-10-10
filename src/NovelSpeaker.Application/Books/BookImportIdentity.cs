namespace NovelSpeaker.Application.Books;

/// <summary>Editable identity defaults and the user's final pre-import confirmation.</summary>
public sealed record BookImportIdentity(string Title, string? Author);
