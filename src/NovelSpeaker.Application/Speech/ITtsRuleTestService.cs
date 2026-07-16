using NovelSpeaker.Domain.Speech;
using NovelSpeaker.Application.Speech.Rules;

namespace NovelSpeaker.Application.Speech;

/// <summary>
/// Drives rule-page试听 flows without exposing HTTP or player details to the UI.
/// </summary>
public interface ITtsRuleTestService
{
    Task<TtsRuleTestResult> TestAsync(
        TtsRuleDraftTestInput input,
        CancellationToken cancellationToken);
}
