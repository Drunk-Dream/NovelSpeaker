using CommunityToolkit.Mvvm.ComponentModel;
using NovelSpeaker.Application.Abstractions;

namespace NovelSpeaker.App.ViewModels;

/// <summary>
/// Exposes the minimal startup state for the application shell.
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    public MainWindowViewModel(IAppDataDirectoryProvider directories)
    {
        StatusText = "NovelSpeaker engineering foundation ready";
        DataDirectoryText = directories.RootDirectoryPath;
    }

    [ObservableProperty]
    private string statusText;

    [ObservableProperty]
    private string dataDirectoryText;
}
