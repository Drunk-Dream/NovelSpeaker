using NovelSpeaker.App.ViewModels;
using Wpf.Ui.Abstractions.Controls;

namespace NovelSpeaker.App.Pages;

public partial class AppearanceSettingsPage : System.Windows.Controls.Page, INavigationAware, INavigableView<AppearanceSettingsViewModel>
{
    public AppearanceSettingsPage(AppearanceSettingsViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = ViewModel;
        InitializeComponent();
    }

    public AppearanceSettingsViewModel ViewModel { get; }

    public Task OnNavigatedToAsync()
    {
        return ViewModel.LoadAsync(CancellationToken.None);
    }

    public Task OnNavigatedFromAsync()
    {
        return Task.CompletedTask;
    }
}
