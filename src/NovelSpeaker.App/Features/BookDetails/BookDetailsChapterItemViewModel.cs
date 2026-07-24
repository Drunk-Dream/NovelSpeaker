namespace NovelSpeaker.App.Features.BookDetails;

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
