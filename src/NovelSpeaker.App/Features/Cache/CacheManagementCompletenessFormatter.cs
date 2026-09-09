using NovelSpeaker.Application.Playback.Cache;

namespace NovelSpeaker.App.Features.Cache;

internal static class CacheManagementCompletenessFormatter
{
    public static string Format(CachedChapterSummary chapter, ChapterCacheStatus status)
    {
        ArgumentNullException.ThrowIfNull(chapter);
        ArgumentNullException.ThrowIfNull(status);

        switch (status.Kind)
        {
            case ChapterCacheStatusKind.PlanMissing:
                return "完整度：计划计算中";
            case ChapterCacheStatusKind.PlanStale:
                return "完整度：计划更新中";
            case ChapterCacheStatusKind.PlanUnavailable:
                return "完整度：计划计算中";
            case ChapterCacheStatusKind.NoPlayableContent:
                return "完整度：无可播放内容";
            case ChapterCacheStatusKind.ConfigurationUnavailable:
                return "完整度：配置不可用";
        }

        if (status.TotalSegmentCount is null)
        {
            return "完整度：配置不可用";
        }

        var totalSegmentCount = status.TotalSegmentCount.Value;
        if (totalSegmentCount <= 0)
        {
            return "完整度：无可播放内容";
        }

        var ratio = Math.Clamp(status.CachedSegmentCount / (double)totalSegmentCount, 0, 1);
        return $"完整度：{status.CachedSegmentCount}/{totalSegmentCount} 段 · {ratio:P0}";
    }
}
