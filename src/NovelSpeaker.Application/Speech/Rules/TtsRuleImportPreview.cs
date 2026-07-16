namespace NovelSpeaker.Application.Speech.Rules;

/// <summary>
/// Represents the result of parsing a JSON import payload before it is committed.
/// </summary>
public sealed record TtsRuleImportPreview(
    string SourceDescription,
    IReadOnlyList<TtsRuleImportItem> Items,
    string? ErrorMessage)
{
    public int ImportableCount => Items.Count(item => item.CanImport);
    public int SkippedCount => Items.Count(item => !item.CanImport);
}
