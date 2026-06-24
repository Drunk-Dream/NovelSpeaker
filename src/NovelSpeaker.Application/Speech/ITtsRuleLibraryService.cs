using NovelSpeaker.Domain.Speech;

namespace NovelSpeaker.Application.Speech;

/// <summary>
/// Coordinates rule import, selection, export, and list projection for the TTS rules page.
/// </summary>
public interface ITtsRuleLibraryService
{
    Task<IReadOnlyList<TtsRuleSummary>> GetRulesAsync(CancellationToken cancellationToken);

    Task<TtsRuleImportPreview> CreateImportPreviewAsync(
        string jsonText,
        string sourceDescription,
        CancellationToken cancellationToken);

    Task<TtsRuleImportResult> ImportAsync(TtsRuleImportPreview preview, CancellationToken cancellationToken);

    Task<string?> ExportRuleJsonAsync(long ruleId, CancellationToken cancellationToken);

    Task SelectRuleAsync(long? ruleId, CancellationToken cancellationToken);

    Task SetRuleEnabledAsync(long ruleId, bool isEnabled, CancellationToken cancellationToken);

    Task DeleteRuleAsync(long ruleId, CancellationToken cancellationToken);
}
