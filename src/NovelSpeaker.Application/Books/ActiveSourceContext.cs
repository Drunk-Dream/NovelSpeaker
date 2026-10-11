namespace NovelSpeaker.Application.Books;

/// <summary>
/// Identifies the active book binding (SourceId) and its CurrentCatalog snapshot.
/// SourceId is a binding ID, never a global source definition ID.
/// Full catalog replacement creates new technical IDs,
/// so the first entry distinguishes snapshots without timestamps or content matching.
/// </summary>
public sealed record ActiveSourceContext(string SourceId, string? CatalogVersion);
