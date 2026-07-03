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

    Task<TtsRuleImportResult> ImportJsonTextAsync(
        string jsonText,
        string sourceDescription,
        CancellationToken cancellationToken);

    Task<TtsRuleImportResult> ImportAsync(TtsRuleImportPreview preview, CancellationToken cancellationToken);

    Task<string?> ExportRuleJsonAsync(long ruleId, CancellationToken cancellationToken);

    Task<TtsRuleEditorModel?> GetEditorAsync(long ruleId, CancellationToken cancellationToken);

    Task<TtsRuleValidationResult> ValidateEditorAsync(TtsRuleEditorModel editor, CancellationToken cancellationToken);

    Task<HttpTtsRule> SaveEditorAsync(TtsRuleEditorModel editor, CancellationToken cancellationToken);

    Task<TtsRuleProtectionInfo> GetRuleProtectionAsync(
        long ruleId,
        TtsRuleMutationAction action,
        CancellationToken cancellationToken);

    Task<TtsRuleMutationResult> ApplyRuleMutationAsync(
        TtsRuleMutationDecision decision,
        CancellationToken cancellationToken);

    Task SelectRuleAsync(long? ruleId, CancellationToken cancellationToken);

    Task SetRuleEnabledAsync(long ruleId, bool isEnabled, CancellationToken cancellationToken);

    Task DeleteRuleAsync(long ruleId, CancellationToken cancellationToken);
}
