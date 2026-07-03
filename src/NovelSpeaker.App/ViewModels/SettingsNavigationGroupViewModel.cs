namespace NovelSpeaker.App.ViewModels;

public sealed record SettingsNavigationGroupViewModel(
    string Title,
    IReadOnlyList<SettingsNavigationItemViewModel> Items);
