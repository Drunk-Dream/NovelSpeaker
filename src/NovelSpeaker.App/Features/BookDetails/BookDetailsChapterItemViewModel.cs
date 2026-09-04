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

    private string _cachePercentageText = string.Empty;

    public string CachePercentageText
    {
        get => _cachePercentageText;
        private set
        {
            if (SetProperty(ref _cachePercentageText, value))
            {
                OnPropertyChanged(nameof(IsCachePercentageVisible));
                OnPropertyChanged(nameof(AutomationName));
            }
        }
    }

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

    internal bool ApplyCacheStatusSilently(int cachedSegmentCount, int? totalSegmentCount)
    {
        var formatted = ChapterCachePercentageFormatter.Format(
            cachedSegmentCount,
            totalSegmentCount);
        if (string.Equals(CachePercentageText, formatted, StringComparison.Ordinal))
        {
            return false;
        }

        _cachePercentageText = formatted;
        return true;
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
