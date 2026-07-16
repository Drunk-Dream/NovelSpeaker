using NovelSpeaker.Domain.Speech;

namespace NovelSpeaker.Application.Speech.Compilation;

/// <inheritdoc />
public sealed class TtsRuleNormalizer : ITtsRuleNormalizer
{
    public NormalizedHttpTtsRule Normalize(HttpTtsRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        return new NormalizedHttpTtsRule(
            rule.Id,
            rule.Name,
            NormalizedTemplate.Parse(rule.Url),
            rule.Headers.ToDictionary(
                pair => pair.Key,
                pair => NormalizedTemplate.Parse(pair.Value),
                StringComparer.OrdinalIgnoreCase),
            rule.RequestMethod,
            rule.RequestBody is null ? null : NormalizedTemplate.Parse(rule.RequestBody),
            rule.RequestBodyIsJsonStructure,
            rule.ContentType,
            rule.ConcurrentRate);
    }
}
