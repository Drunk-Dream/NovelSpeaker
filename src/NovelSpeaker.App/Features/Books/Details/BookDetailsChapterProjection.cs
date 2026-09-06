using NovelSpeaker.App.Shared.Presentation.Cache;

namespace NovelSpeaker.App.Features.Books.Details;

/// <summary>
/// Immutable stable chapter fields plus the sparse decorations currently shown for one row.
/// </summary>
public sealed class BookDetailsChapterProjection
{
    public BookDetailsChapterProjection(
        int chapterIndex,
        string title,
        bool isCurrent = false,
        string cachePercentageText = "")
    {
        ChapterIndex = chapterIndex;
        Title = title;
        IsCurrent = isCurrent;
        CachePercentageText = cachePercentageText;
    }

    public int ChapterIndex { get; }

    public string IndexText => $"第 {ChapterIndex + 1} 章";

    public string Title { get; }

    public bool IsCurrent { get; }

    public string TitleToolTip => Title;

    public string CachePercentageText { get; }

    public bool IsCachePercentageVisible => !string.IsNullOrEmpty(CachePercentageText);

    public string AutomationName => IsCurrent
        ? BuildAutomationName("当前章节")
        : BuildAutomationName();

    public BookDetailsChapterProjection WithCurrentState(bool isCurrent) =>
        new(ChapterIndex, Title, isCurrent, CachePercentageText);

    public BookDetailsChapterProjection WithCacheStatus(int cachedSegmentCount, int? totalSegmentCount) =>
        new(
            ChapterIndex,
            Title,
            IsCurrent,
            ChapterCachePercentageFormatter.Format(cachedSegmentCount, totalSegmentCount));

    private string BuildAutomationName(string? state = null)
    {
        var cacheState = IsCachePercentageVisible ? $"缓存进度 {CachePercentageText}" : null;
        var states = new[] { state, cacheState }.Where(static item => item is not null);
        var suffix = string.Join('，', states);
        return string.IsNullOrEmpty(suffix)
            ? $"{IndexText}，{Title}"
            : $"{IndexText}，{Title}，{suffix}";
    }
}
