using CommunityToolkit.Mvvm.ComponentModel;
using NovelSpeaker.App.Navigation;

namespace NovelSpeaker.App.ViewModels;

/// <summary>
/// Projects shell-level navigation state for the main window.
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly IAppNavigationService _navigationService;

    public MainWindowViewModel(IAppNavigationService navigationService)
    {
        _navigationService = navigationService;
        _navigationService.CurrentEntryChanged += OnCurrentEntryChanged;
        ApplyNavigationState(_navigationService.CurrentEntry);
    }

    [ObservableProperty]
    private AppPrimaryDestination selectedPrimaryDestination;

    [ObservableProperty]
    private bool canGoBack;

    [ObservableProperty]
    private bool isPlaybackShortcutVisible;

    private void OnCurrentEntryChanged(object? sender, AppNavigationChangedEventArgs e)
    {
        ApplyNavigationState(e.Entry);
    }

    private void ApplyNavigationState(AppNavigationEntry entry)
    {
        SelectedPrimaryDestination = entry.PrimaryDestination;
        CanGoBack = _navigationService.CanGoBack;
        IsPlaybackShortcutVisible = false;
    }
}
