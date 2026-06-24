namespace NovelSpeaker.App.ViewModels;

public sealed record PlayerRuleItemViewModel(
    long Id,
    string Name,
    bool IsEnabled,
    bool IsSelected);
