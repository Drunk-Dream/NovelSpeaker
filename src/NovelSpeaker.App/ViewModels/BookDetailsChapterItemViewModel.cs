namespace NovelSpeaker.App.ViewModels;

public sealed record BookDetailsChapterItemViewModel(
    int ChapterIndex,
    string IndexText,
    string Title,
    string RangeText,
    bool IsCurrent);
