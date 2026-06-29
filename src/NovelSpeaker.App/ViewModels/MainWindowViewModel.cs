using CommunityToolkit.Mvvm.ComponentModel;

namespace NovelSpeaker.App.ViewModels;

/// <summary>
/// Projects shell-level navigation state for the main window.
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    public MainWindowViewModel()
    {
    }

    [ObservableProperty]
    private bool isPlaybackShortcutVisible;
}
