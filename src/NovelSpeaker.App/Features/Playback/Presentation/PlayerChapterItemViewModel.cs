using CommunityToolkit.Mvvm.ComponentModel;
using NovelSpeaker.App.Shared.Presentation.Cache;

namespace NovelSpeaker.App.Features.Playback.Presentation;

public sealed partial class PlayerChapterItemViewModel : ObservableObject
{
    public PlayerChapterItemViewModel(int chapterIndex, string title)
    {
        ChapterIndex = chapterIndex;
        Title = title;
    }

    public int ChapterIndex { get; }

    public string Title { get; }

    [ObservableProperty]
    private bool isCurrent;

    [ObservableProperty]
    private bool isSelectedForActiveCache;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCachePercentageVisible))]
    [NotifyPropertyChangedFor(nameof(AutomationName))]
    private string cachePercentageText = string.Empty;

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

    public void ApplyCacheStatus(int cachedSegmentCount, int? totalSegmentCount)
    {
        CachePercentageText = ChapterCachePercentageFormatter.Format(
            cachedSegmentCount,
            totalSegmentCount);
    }

    partial void OnIsCurrentChanged(bool value)
    {
        OnPropertyChanged(nameof(AutomationName));
    }

    partial void OnIsSelectedForActiveCacheChanged(bool value)
    {
        OnPropertyChanged(nameof(AutomationName));
    }
}
