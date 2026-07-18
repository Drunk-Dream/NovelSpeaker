using NovelSpeaker.Domain.Speech;

namespace NovelSpeaker.Application.Speech.Rules;

/// <summary>Owns TTS rule editing copies, validation, persistence, and draft export.</summary>
public interface ITtsRuleEditorUseCase
{
    Task<TtsRuleEditorModel?> GetEditorAsync(long ruleId, CancellationToken cancellationToken);

    Task<TtsRuleValidationResult> ValidateEditorAsync(TtsRuleEditorModel editor, CancellationToken cancellationToken);

    Task<TtsRuleDraftPreparationResult> PrepareDraftAsync(TtsRuleEditorModel editor, CancellationToken cancellationToken);

    Task<HttpTtsRule> SaveEditorAsync(TtsRuleEditorModel editor, CancellationToken cancellationToken);

    Task<string> ExportEditorJsonAsync(TtsRuleEditorModel editor, CancellationToken cancellationToken);
}
