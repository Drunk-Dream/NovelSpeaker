using NovelSpeaker.Domain.Speech;

namespace NovelSpeaker.Application.Speech.Compilation;

/// <summary>
/// Parses persisted rule templates into the runtime compilation model.
/// </summary>
public interface ITtsRuleNormalizer
{
    NormalizedHttpTtsRule Normalize(HttpTtsRule rule);
}
