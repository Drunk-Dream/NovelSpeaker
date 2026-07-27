namespace NovelSpeaker.App.Shared.Presentation.Cache;

internal static class ChapterCachePercentageFormatter
{
    public static string Format(int cachedSegmentCount, int? totalSegmentCount)
    {
        if (cachedSegmentCount <= 0 || totalSegmentCount is null or <= 0)
        {
            return string.Empty;
        }

        var percentage = (int)Math.Round(
            Math.Clamp(cachedSegmentCount / (double)totalSegmentCount.Value, 0d, 1d) * 100d,
            MidpointRounding.AwayFromZero);
        return percentage > 0 ? $"{percentage}%" : string.Empty;
    }
}
