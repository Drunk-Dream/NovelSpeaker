using CommunityToolkit.Mvvm.ComponentModel;

namespace NovelSpeaker.App.ViewModels;

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
}
