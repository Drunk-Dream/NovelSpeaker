using NovelSpeaker.Domain.Speech;

namespace NovelSpeaker.Application.Speech;

/// <summary>
/// Represents the full editable model used by rule management UI.
/// </summary>
public sealed record TtsRuleEditorModel(
    long? Id,
    string Name,
    bool IsEnabled,
    string Url,
    string? ContentType,
    string? ConcurrentRate,
    bool EnabledCookieJar,
    long? LastUpdateTime,
    IReadOnlyList<TtsRuleEditorKeyValue> Headers,
    TtsRuleRequestOptionsEditor RequestOptions,
    string RawRuleJson,
    TtsRuleCompatibilityStatus CompatibilityStatus,
    IReadOnlyList<string> UnsupportedFields)
{
    public IReadOnlyList<TtsRuleEditorKeyValue> LoginInfo { get; init; } = [];
}
