namespace NovelSpeaker.Application.Cache;

/// <summary>A committed display change, without Cache-internal invalidation reasons.</summary>
public sealed record CacheReadModelChange(
    long Revision,
    IReadOnlyList<CacheReadModelScope> Scopes,
    bool OverviewChanged = false);

public abstract record CacheReadModelScope
{
    private CacheReadModelScope() { }

    public sealed record Global : CacheReadModelScope;

    public sealed record Book(string BookId) : CacheReadModelScope;

    public sealed record Chapters(string BookId, IReadOnlyList<int> ChapterIndices) : CacheReadModelScope;
}
