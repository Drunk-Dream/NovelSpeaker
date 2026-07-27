using NovelSpeaker.App.Shell.Activation;
using Wpf.Ui.Abstractions.Controls;

namespace NovelSpeaker.App.Features.Appearance;

public partial class AppearanceSettingsPage : System.Windows.Controls.Page, INavigationAware, INavigableView<AppearanceSettingsViewModel>
{
    private readonly PageActivationController _activation = new();

    public AppearanceSettingsPage(AppearanceSettingsViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = ViewModel;
        InitializeComponent();
    }

    public AppearanceSettingsViewModel ViewModel { get; }

    public async Task OnNavigatedToAsync()
    {
        var activation = _activation.Activate();
        ViewModel.Activate(activation);
        activation.Register(ViewModel.Deactivate);
        try
        {
            await ViewModel.LoadAsync(activation.CancellationToken);
        }
        catch (OperationCanceledException) when (!activation.IsCurrent)
        {
        }
    }

    public Task OnNavigatedFromAsync()
    {
        _activation.Deactivate();
        return Task.CompletedTask;
    }
}
