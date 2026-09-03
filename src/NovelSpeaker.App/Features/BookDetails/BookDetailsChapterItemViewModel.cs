using CommunityToolkit.Mvvm.ComponentModel;
using NovelSpeaker.App.Shared.Presentation.Cache;

namespace NovelSpeaker.App.Features.BookDetails;

public sealed partial class BookDetailsChapterItemViewModel : ObservableObject
{
    public BookDetailsChapterItemViewModel(
        int chapterIndex,
        string indexText,
        string title,
        bool isCurrent)
    {
        ChapterIndex = chapterIndex;
        IndexText = indexText;
        Title = title;
        IsCurrent = isCurrent;
    }

    public int ChapterIndex { get; }

    public string IndexText { get; }

    public string Title { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AutomationName))]
    private bool isCurrent;

    public void ApplyCurrentState(bool value)
    {
        IsCurrent = value;
    }

    public string TitleToolTip => Title;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCachePercentageVisible))]
    [NotifyPropertyChangedFor(nameof(AutomationName))]
    private string cachePercentageText = string.Empty;

    public bool IsCachePercentageVisible => !string.IsNullOrEmpty(CachePercentageText);

    public string AutomationName => IsCurrent
        ? BuildAutomationName("当前章节")
        : BuildAutomationName();

    public void ApplyCacheStatus(int cachedSegmentCount, int? totalSegmentCount)
    {
        CachePercentageText = ChapterCachePercentageFormatter.Format(
            cachedSegmentCount,
            totalSegmentCount);
    }

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
