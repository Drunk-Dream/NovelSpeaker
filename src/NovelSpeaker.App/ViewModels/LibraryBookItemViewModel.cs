namespace NovelSpeaker.App.ViewModels;

public sealed record LibraryBookItemViewModel(
    string Id,
    string Title,
    string? Author,
    string CurrentChapterTitle,
    string ImportedAt,
    string? LastPlayedAt = null);
