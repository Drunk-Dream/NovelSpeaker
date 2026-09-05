using NovelSpeaker.App.Shared.Presentation.Cache;

namespace NovelSpeaker.App.Features.Playback.Presentation;

public sealed class PlayerChapterItemViewModel
{
    public PlayerChapterItemViewModel(
        int chapterIndex,
        string title,
        bool isCurrent = false,
        bool isSelectedForActiveCache = false,
        string cachePercentageText = "")
    {
        ChapterIndex = chapterIndex;
        Title = title;
        IsCurrent = isCurrent;
        IsSelectedForActiveCache = isSelectedForActiveCache;
        CachePercentageText = cachePercentageText;
    }

    public int ChapterIndex { get; }

    public string Title { get; }

    public bool IsCurrent { get; }

    public bool IsSelectedForActiveCache { get; }

    public string CachePercentageText { get; }

    public bool IsCachePercentageVisible => !string.IsNullOrEmpty(CachePercentageText);

    public string AutomationName
    {
        get
        {
            var states = new List<string>();
            if (IsCurrent)
            {
                states.Add("当前章节");
            }

            if (IsSelectedForActiveCache)
            {
                states.Add("已选择缓存");
            }

            if (IsCachePercentageVisible)
            {
                states.Add($"缓存进度 {CachePercentageText}");
            }

            return states.Count == 0
                ? Title
                : $"{Title}，{string.Join('，', states)}";
        }
    }

    public PlayerChapterItemViewModel WithCurrentState(bool isCurrent) =>
        new(ChapterIndex, Title, isCurrent, IsSelectedForActiveCache, CachePercentageText);

    public PlayerChapterItemViewModel WithActiveCacheSelection(bool isSelected) =>
        new(ChapterIndex, Title, IsCurrent, isSelected, CachePercentageText);

    public PlayerChapterItemViewModel WithCacheStatus(int cachedSegmentCount, int? totalSegmentCount) =>
        new(
            ChapterIndex,
            Title,
            IsCurrent,
            IsSelectedForActiveCache,
            ChapterCachePercentageFormatter.Format(cachedSegmentCount, totalSegmentCount));
}
