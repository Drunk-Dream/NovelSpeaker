using NovelSpeaker.App.Shell.Activation;
using Wpf.Ui.Abstractions.Controls;

namespace NovelSpeaker.App.Features.Settings;

public partial class SettingsPage : System.Windows.Controls.Page, INavigationAware, INavigableView<SettingsViewModel>
{
    private readonly PageActivationController _activation = new();

    public SettingsPage(SettingsViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        DataContext = ViewModel;
    }

    public SettingsViewModel ViewModel { get; }

    public Task OnNavigatedToAsync()
    {
        _activation.Activate();
        return Task.CompletedTask;
    }

    public Task OnNavigatedFromAsync()
    {
        _activation.Deactivate();
        return Task.CompletedTask;
    }
}
