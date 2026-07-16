namespace NovelSpeaker.Domain.Speech;

/// <summary>
/// Represents one persisted HTTP TTS rule without transport or persistence encoding details.
/// </summary>
public sealed record HttpTtsRule(
    long Id,
    string Name,
    string Url,
    string? ContentType,
    string? ConcurrentRate,
    IReadOnlyDictionary<string, string> Headers,
    string? RequestMethod,
    string? RequestBody,
    bool RequestBodyIsJsonStructure,
    long? LastUpdateTime,
    bool IsEnabled,
    DateTimeOffset? LastUsedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
