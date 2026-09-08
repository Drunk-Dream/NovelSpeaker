namespace NovelSpeaker.App.Features.Rules.Shared;

/// <summary>
/// Normalizes import counts for rule features with different application use cases.
/// </summary>
internal sealed record RuleImportResult(
    int ImportedCount,
    int SkippedCount,
    int TotalCount,
    int FailedCount = 0);
