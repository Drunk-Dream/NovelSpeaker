namespace NovelSpeaker.App.Features.Settings;

public sealed record SettingsNavigationGroupViewModel(
    string Title,
    IReadOnlyList<SettingsNavigationItemViewModel> Items);
