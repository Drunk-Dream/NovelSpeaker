using NovelSpeaker.App.Shared.Presentation.Cache;

namespace NovelSpeaker.App.Features.Playback.Presentation;

public sealed class PlayerChapterItemViewModel
{
    public PlayerChapterItemViewModel(
        int chapterIndex,
        string title,
        bool isCurrent = false,
        bool isSelected = false,
        string cachePercentageText = "")
    {
        ChapterIndex = chapterIndex;
        Title = title;
        IsCurrent = isCurrent;
        IsSelected = isSelected;
        CachePercentageText = cachePercentageText;
    }

    public int ChapterIndex { get; }

    public string Title { get; }

    public bool IsCurrent { get; }

    public bool IsSelected { get; }

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

            if (IsSelected)
            {
                states.Add("已选择");
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
        new(ChapterIndex, Title, isCurrent, IsSelected, CachePercentageText);

    public PlayerChapterItemViewModel WithSelection(bool isSelected) =>
        new(ChapterIndex, Title, IsCurrent, isSelected, CachePercentageText);

    public PlayerChapterItemViewModel WithCacheStatus(int cachedSegmentCount, int? totalSegmentCount) =>
        new(
            ChapterIndex,
            Title,
            IsCurrent,
            IsSelected,
            ChapterCachePercentageFormatter.Format(cachedSegmentCount, totalSegmentCount));
}
