namespace NovelSpeaker.Domain.Speech;

/// <summary>
/// Persists an imported HTTP TTS rule together with the raw JSON that produced it.
/// </summary>
public sealed record HttpTtsRule(
    long Id,
    string Name,
    string Url,
    string? ContentType,
    string? ConcurrentRate,
    string? Header,
    string? LoginUrl,
    string? LoginUi,
    bool EnabledCookieJar,
    string? LoginCheckJs,
    string? JsLib,
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
            Url,
            ContentType,
            ConcurrentRate,
            Header,
            EnabledCookieJar,
            UnsupportedFields);
    }
}
