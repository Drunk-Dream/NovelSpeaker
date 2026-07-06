namespace NovelSpeaker.App.ViewModels;

public sealed record BookDetailsChapterItemViewModel(
    int ChapterIndex,
    string IndexText,
    string Title,
    bool IsCurrent)
{
    public string TitleToolTip => Title;

    public string AutomationName => IsCurrent
        ? $"{IndexText}，{Title}，当前章节"
        : $"{IndexText}，{Title}";
}
