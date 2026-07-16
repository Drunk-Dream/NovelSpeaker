namespace NovelSpeaker.Application.Speech.Rules;

/// <summary>
/// Summarizes the persisted outcome of a confirmed rule import.
/// </summary>
public sealed record TtsRuleImportResult(
    int ImportedCount,
    int SkippedCount,
    int TotalCount)
{
    public int FailedCount { get; init; }

    public long? FirstImportedRuleId { get; init; }
}
