namespace NovelSpeaker.Application.Books;

/// <summary>Summarizes a merge import without exposing persistence identities.</summary>
public sealed record RuleJsonImportResult(int ImportedCount, int SkippedCount, int TotalCount);
