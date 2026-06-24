using NovelSpeaker.Domain.Speech;

namespace NovelSpeaker.Application.Speech;

/// <summary>
/// Compiles one normalized rule plus runtime values into a concrete HTTP request and redacted preview.
/// </summary>
public interface ITtsRequestCompiler
{
    Task<TtsRequestCompilationResult> CompileAsync(
        NormalizedHttpTtsRule rule,
        TtsRuleContext context,
        CancellationToken cancellationToken);
}
