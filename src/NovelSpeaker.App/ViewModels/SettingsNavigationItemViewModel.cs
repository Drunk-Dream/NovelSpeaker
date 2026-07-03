using System.Windows.Input;

namespace NovelSpeaker.App.ViewModels;

public sealed record SettingsNavigationItemViewModel(
    string Title,
    ICommand Command);
