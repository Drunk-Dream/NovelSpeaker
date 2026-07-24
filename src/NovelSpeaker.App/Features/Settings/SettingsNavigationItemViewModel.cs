using System.Windows.Input;

namespace NovelSpeaker.App.Features.Settings;

public sealed record SettingsNavigationItemViewModel(
    string Title,
    SettingsNavigationIcon Icon,
    ICommand Command);
