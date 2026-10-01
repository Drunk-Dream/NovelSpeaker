using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using NovelSpeaker.App.Shared.Presentation.Selection;
using NovelSpeaker.App.Shell.Activation;
using NovelSpeaker.App.Shell.Navigation;
using Wpf.Ui.Abstractions.Controls;

namespace NovelSpeaker.App.Features.SpeechServices;

public partial class SpeechServicesPage : System.Windows.Controls.Page, INavigationAware, INavigableView<SpeechServicesViewModel>
{
    public ICommand SelectionCommand { get; }

    private readonly PageActivationController _activation = new();
    private readonly INavigationGuardService _navigationGuardService;
    private readonly PageEventOperationRunner _eventOperations;

    public SpeechServicesPage(
        SpeechServicesViewModel viewModel,
        INavigationGuardService navigationGuardService,
        PageEventOperationRunner eventOperations)
        : this()
    {
        ViewModel = viewModel;
        _navigationGuardService = navigationGuardService;
        _eventOperations = eventOperations;
        DataContext = ViewModel;
    }

    internal SpeechServicesPage()
    {
        ViewModel = null!;
        _navigationGuardService = null!;
        _eventOperations = PageEventOperationRunner.DesignTime;
        SelectionCommand = new AsyncRelayCommand<SpeechProviderListItemViewModel>(SelectWithModifiersAsync);
        InitializeComponent();
    }

    public SpeechServicesViewModel ViewModel { get; }

    private Task SelectWithModifiersAsync(SpeechProviderListItemViewModel? item)
    {
        var modifiers = DesktopSelectionInput.ReadModifiers();
        return _eventOperations.RunAsync(_activation, "选择语音服务失败",
            token => ViewModel.SelectProviderWithModifiersAsync(item, modifiers, token));
    }

    public async Task OnNavigatedToAsync()
    {
        using var operation = _eventOperations.StartCriticalLoad(NovelSpeaker.Application.Observability.OperationCatalog.UiRulesLoad);
        var activation = _activation.Activate();
        activation.Register(ViewModel.HandleNavigatedFrom);
        activation.Register(_navigationGuardService.Register(ViewModel.ConfirmLeaveAsync));

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

    public async Task OnNavigatedFromAsync()
    {
        _activation.Deactivate();
        await ViewModel.FinishDeactivationAsync();
    }

}
