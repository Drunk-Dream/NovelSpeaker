using NovelSpeaker.Application.Playback.Cache;
using NovelSpeaker.App.Features.Cache;
using Xunit;

namespace NovelSpeaker.App.PresentationTests;

public sealed class CacheCompletenessFormatterTests
{
    [Fact]
    public void Cache_management_formatter_keeps_cached_chapter_visible_at_zero_percent()
    {
        var chapter = CreateChapter();

        Assert.Equal(
            "完整度：0/8 段 · 0%",
            CacheManagementCompletenessFormatter.Format(chapter, new ChapterCacheStatus(0, 0, 8)));
    }

    [Fact]
    public void Cache_management_formatter_reports_stale_plan_as_updating()
    {
        var chapter = CreateChapter();

        Assert.Equal(
            "完整度：计划更新中",
            CacheManagementCompletenessFormatter.Format(
                chapter,
                new ChapterCacheStatus(0, 0, null) { Kind = ChapterCacheStatusKind.PlanStale }));
    }

    [Fact]
    public void Cache_management_formatter_reports_missing_plan_as_calculating()
    {
        var chapter = CreateChapter();

        Assert.Equal(
            "完整度：计划计算中",
            CacheManagementCompletenessFormatter.Format(
                chapter,
                new ChapterCacheStatus(0, 0, null) { Kind = ChapterCacheStatusKind.PlanMissing }));
    }

    private static CachedChapterSummary CreateChapter() =>
        new("book-1", 0, "第一章", 0, 3, 1024);
}
