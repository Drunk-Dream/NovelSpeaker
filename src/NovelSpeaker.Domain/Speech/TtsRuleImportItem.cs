namespace NovelSpeaker.Domain.Speech;

/// <summary>
/// Represents a single candidate inside an import preview.
/// </summary>
public sealed record TtsRuleImportItem(
    int Index,
    string Name,
    string Url,
    TtsRuleCompatibilityStatus CompatibilityStatus,
    IReadOnlyList<string> UnsupportedFields,
    bool CanImport,
    bool IsDuplicate,
    bool HasSameNameConflict,
    string StatusMessage,
    HttpTtsRule CandidateRule)
{
    public string UnsupportedFieldsSummary => UnsupportedFields.Count == 0
        ? "无"
        : string.Join("、", UnsupportedFields);
}
