using NovelSpeaker.App.ViewModels;
using Wpf.Ui;
using Wpf.Ui.Abstractions.Controls;

namespace NovelSpeaker.App.Pages;

public partial class SettingsPage : System.Windows.Controls.Page, INavigationAware, INavigableView<SettingsViewModel>
{
    public SettingsPage(SettingsViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        SettingsView.DataContext = ViewModel;
    }

    public SettingsViewModel ViewModel { get; }

    public Task OnNavigatedToAsync()
    {
        return Task.CompletedTask;
    }

    public Task OnNavigatedFromAsync()
    {
        return Task.CompletedTask;
    }
}
