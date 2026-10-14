using NovelSpeaker.Application.Cache;
using NovelSpeaker.App.Shared.Presentation.Cache;

namespace NovelSpeaker.App.Features.Cache;

internal static class CacheManagementCompletenessFormatter
{
    public static string Format(ChapterCacheStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        if (status.Kind != ChapterCacheStatusKind.Available) return string.Empty;
        var percentage = ChapterCachePercentageFormatter.Format(status.CachedSegmentCount, status.TotalSegmentCount);
        return percentage.Length == 0 ? string.Empty : $"完整度：{percentage}";
    }
}
