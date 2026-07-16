namespace NovelSpeaker.Application.Speech.Compilation;

/// <summary>
/// Represents the runtime view of a converted HTTP TTS rule.
/// </summary>
public sealed record NormalizedHttpTtsRule(
    long RuleId,
    string Name,
    NormalizedTemplate UrlTemplate,
    IReadOnlyDictionary<string, NormalizedTemplate> HeaderTemplates,
    string? RequestMethod,
    NormalizedTemplate? RequestBodyTemplate,
    bool RequestBodyIsJsonStructure,
    string? DeclaredContentType,
    string? ConcurrentRate);
