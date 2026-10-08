using NovelSpeaker.Domain.Books;

namespace NovelSpeaker.Application.Books;

/// <summary>Detached metadata of the explicitly active source, without typed storage.</summary>
public sealed record ActiveSourceSummary(
    ActiveSourceContext Context,
    SourceType Type,
    string Title,
    string? Author,
    string? Description);
