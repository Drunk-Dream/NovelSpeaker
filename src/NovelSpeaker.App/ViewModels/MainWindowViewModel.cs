using CommunityToolkit.Mvvm.ComponentModel;

namespace NovelSpeaker.App.ViewModels;

/// <summary>
/// Hosts the top-level pages for the desktop shell.
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    public MainWindowViewModel(
        LibraryViewModel libraryViewModel,
        PlayerViewModel playerViewModel,
        TtsRulesViewModel ttsRulesViewModel,
        SettingsViewModel settingsViewModel)
    {
        Library = libraryViewModel;
        Player = playerViewModel;
        Rules = ttsRulesViewModel;
        Settings = settingsViewModel;
        CurrentPage = Library;
    }

    public LibraryViewModel Library { get; }
    public PlayerViewModel Player { get; }
    public TtsRulesViewModel Rules { get; }
    public SettingsViewModel Settings { get; }

    [ObservableProperty]
    private object currentPage;

    public void ShowLibrary() => CurrentPage = Library;
    public void ShowPlayer() => CurrentPage = Player;
    public void ShowRules() => CurrentPage = Rules;
    public void ShowSettings() => CurrentPage = Settings;
}
