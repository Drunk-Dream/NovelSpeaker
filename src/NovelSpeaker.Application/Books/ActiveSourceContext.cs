namespace NovelSpeaker.Application.Books;

/// <summary>
/// Identifies a source snapshot. Full catalog replacement creates new technical IDs,
/// so the first entry distinguishes snapshots without timestamps or content matching.
/// </summary>
public sealed record ActiveSourceContext(string SourceId, string? CatalogVersion);
