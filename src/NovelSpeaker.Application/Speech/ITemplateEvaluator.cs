using NovelSpeaker.Domain.Speech;
using NovelSpeaker.Application.Speech.Compilation;

namespace NovelSpeaker.Application.Speech;

/// <summary>
/// Evaluates converted rule templates inside the application's restricted JavaScript environment.
/// </summary>
public interface ITemplateEvaluator
{
    Task<string> EvaluateAsync(
        NormalizedTemplate template,
        TtsRuleContext context,
        CancellationToken cancellationToken);
}
