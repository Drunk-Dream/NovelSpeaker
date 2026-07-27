using System.Windows.Input;
using NovelSpeaker.App.Shared.Theming;

namespace NovelSpeaker.App.Features.Settings;

public sealed record SettingsNavigationItemViewModel(
    string Title,
    SettingsNavigationIcon Icon,
    ICommand Command);
