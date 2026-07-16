namespace NovelSpeaker.Infrastructure.Speech.Rules;

internal sealed record TtsRuleRow(
    long Id,
    string Name,
    string Url,
    string? ContentType,
    string? ConcurrentRate,
    string? Header,
    string? RequestOptionsJson,
    long? LastUpdateTime,
    bool IsEnabled,
    string? LastUsedAt,
    string CreatedAt,
    string UpdatedAt);
