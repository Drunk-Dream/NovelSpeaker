using NovelSpeaker.Domain.Speech;

namespace NovelSpeaker.Application.Speech;

/// <summary>
/// Drives rule-page preview,试听, and cookie-reset flows without exposing HTTP or player details to the UI.
/// </summary>
public interface ITtsRuleTestService
{
    Task<TtsRuleTestPreviewResult> CreatePreviewAsync(
        TtsRuleTestInput input,
        CancellationToken cancellationToken);

    Task<TtsRuleTestResult> TestAsync(
        TtsRuleTestInput input,
        CancellationToken cancellationToken);

    Task ClearRuleCookiesAsync(
        long ruleId,
        CancellationToken cancellationToken);
}
