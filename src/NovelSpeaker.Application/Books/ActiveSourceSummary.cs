using NovelSpeaker.Domain.Books;

namespace NovelSpeaker.Application.Books;

/// <summary>The explicitly active binding and catalog context, without book identity or typed storage.</summary>
public sealed record ActiveSourceSummary(
    ActiveSourceContext Context,
    SourceType Type);
