namespace NovelSpeaker.Domain.Speech;

/// <summary>
/// Persists a converted NovelSpeaker HTTP TTS rule and its canonical rule JSON.
/// </summary>
public sealed record HttpTtsRule(
    long Id,
    string Name,
    string Url,
    string? ContentType,
    string? ConcurrentRate,
    string? Header,
    string? RequestOptionsJson,
    bool EnabledCookieJar,
    long? LastUpdateTime,
    string RuleJson,
    bool IsEnabled,
    TtsRuleCompatibilityStatus CompatibilityStatus,
    IReadOnlyList<string> UnsupportedFields,
    string? LastUsedAt,
    string CreatedAt,
    string UpdatedAt)
{
    public NormalizedHttpTtsRule ToNormalizedRule()
    {
        return new NormalizedHttpTtsRule(
            Id,
            Name,
            NormalizedTemplate.Parse(Url),
            Header is null ? null : NormalizedTemplate.Parse(Header),
            RequestOptionsJson is null ? null : NormalizedTemplate.Parse(RequestOptionsJson),
            ContentType,
            ConcurrentRate,
            EnabledCookieJar,
            UnsupportedFields);
    }
}
