using CommunityToolkit.Mvvm.ComponentModel;

namespace NovelSpeaker.App.ViewModels;

public sealed partial class PlayerSegmentItemViewModel : ObservableObject
{
    public PlayerSegmentItemViewModel(int chapterIndex, int segmentIndex, string text)
    {
        ChapterIndex = chapterIndex;
        SegmentIndex = segmentIndex;
        Text = text;
    }

    public int ChapterIndex { get; }

    public int SegmentIndex { get; }

    public string Text { get; }

    [ObservableProperty]
    private bool isCurrent;

    [ObservableProperty]
    private double opacity = 0.52d;
}
