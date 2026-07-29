using NovelSpeaker.Application.Playback.Cache;
using NovelSpeaker.App.Features.Cache;
using NovelSpeaker.App.Shared.Presentation.Cache;
using Xunit;

namespace NovelSpeaker.App.PresentationTests;

public sealed class CacheCompletenessFormatterTests
{
    [Fact]
    public void Directory_formatter_hides_zero_percent_and_unavailable_states()
    {
        Assert.Equal(string.Empty, ChapterCachePercentageFormatter.Format(0, 8));
        Assert.Equal(string.Empty, ChapterCachePercentageFormatter.Format(0, null));
    }

    [Fact]
    public void Cache_management_formatter_keeps_cached_chapter_visible_at_zero_percent()
    {
        var chapter = new CachedChapterCacheItem(
            "book-1",
            0,
            "第一章",
            0,
            3,
            1024,
            8);

        Assert.Equal("完整度：0/8 段 · 0%", CacheManagementCompletenessFormatter.Format(chapter));
    }

    [Fact]
    public void Cache_management_formatter_reports_stale_plan_as_updating()
    {
        var chapter = new CachedChapterCacheItem(
            "book-1",
            0,
            "第一章",
            0,
            3,
            1024,
            null)
        {
            CurrentConfigurationStatus = ChapterCacheStatusKind.PlanStale
        };

        Assert.Equal("完整度：计划更新中", CacheManagementCompletenessFormatter.Format(chapter));
    }

    [Fact]
    public void Cache_management_formatter_reports_missing_plan_as_calculating()
    {
        var chapter = new CachedChapterCacheItem(
            "book-1",
            0,
            "第一章",
            0,
            3,
            1024,
            null)
        {
            CurrentConfigurationStatus = ChapterCacheStatusKind.PlanMissing
        };

        Assert.Equal("完整度：计划计算中", CacheManagementCompletenessFormatter.Format(chapter));
    }
}
