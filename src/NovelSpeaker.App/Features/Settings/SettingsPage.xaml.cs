using NovelSpeaker.App.Shell.Activation;
using Wpf.Ui.Abstractions.Controls;

namespace NovelSpeaker.App.Features.Settings;

public partial class SettingsPage : System.Windows.Controls.Page, INavigationAware, INavigableView<SettingsViewModel>
{
    private readonly PageActivationController _activation = new();
    private readonly PageEventOperationRunner _eventOperations;

    public SettingsPage(
        SettingsViewModel viewModel,
        PageEventOperationRunner? eventOperations = null)
    {
        ViewModel = viewModel;
        _eventOperations = eventOperations ?? PageEventOperationRunner.DesignTime;
        InitializeComponent();
        DataContext = ViewModel;
    }

    public SettingsViewModel ViewModel { get; }

    public Task OnNavigatedToAsync()
    {
        _activation.Activate();
        using var operation = _eventOperations.StartCriticalLoad();
        operation.Complete(NovelSpeaker.Application.Observability.OperationResult.Succeeded());
        return Task.CompletedTask;
    }

    public Task OnNavigatedFromAsync()
    {
        _activation.Deactivate();
        return Task.CompletedTask;
    }
}
