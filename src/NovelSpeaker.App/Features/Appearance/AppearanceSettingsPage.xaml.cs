using NovelSpeaker.App.Shell.Activation;
using Wpf.Ui.Abstractions.Controls;

namespace NovelSpeaker.App.Features.Appearance;

public partial class AppearanceSettingsPage : System.Windows.Controls.Page, INavigationAware, INavigableView<AppearanceSettingsViewModel>
{
    private readonly PageActivationController _activation = new();
    private readonly PageEventOperationRunner _eventOperations;

    public AppearanceSettingsPage(
        AppearanceSettingsViewModel viewModel,
        PageEventOperationRunner? eventOperations = null)
    {
        ViewModel = viewModel;
        _eventOperations = eventOperations ?? PageEventOperationRunner.DesignTime;
        DataContext = ViewModel;
        InitializeComponent();
    }

    public AppearanceSettingsViewModel ViewModel { get; }

    public async Task OnNavigatedToAsync()
    {
        using var operation = _eventOperations.StartCriticalLoad(NovelSpeaker.Application.Observability.OperationCatalog.UiSettingsLoad);
        var activation = _activation.Activate();
        ViewModel.Activate(activation);
        activation.Register(ViewModel.Deactivate);
        try
        {
            await ViewModel.LoadAsync(activation.CancellationToken);
            operation.Complete(NovelSpeaker.Application.Observability.OperationResult.Succeeded());
        }
        catch (OperationCanceledException) when (!activation.IsCurrent)
        {
            operation.Complete(NovelSpeaker.Application.Observability.OperationResult.Cancelled());
        }
        catch
        {
            operation.Complete(NovelSpeaker.Application.Observability.OperationResult.Failed("page-load-failed"));
            throw;
        }
    }

    public Task OnNavigatedFromAsync()
    {
        _activation.Deactivate();
        return Task.CompletedTask;
    }
}
