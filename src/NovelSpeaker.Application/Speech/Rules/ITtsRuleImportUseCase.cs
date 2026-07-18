namespace NovelSpeaker.Application.Speech.Rules;

/// <summary>Imports typed TTS rule sources and commits accepted business rules.</summary>
public interface ITtsRuleImportUseCase
{
    Task<TtsRuleImportPreview> CreateImportPreviewAsync(string jsonText, string sourceDescription, CancellationToken cancellationToken);

    Task<TtsRuleImportResult> ImportJsonTextAsync(string jsonText, string sourceDescription, CancellationToken cancellationToken);

    Task<TtsRuleImportResult> ImportAsync(TtsRuleImportPreview preview, CancellationToken cancellationToken);
}
