using System.Windows;
using NovelSpeaker.App.Shell.Activation;
using NovelSpeaker.App.Shell.Navigation;
using Wpf.Ui.Abstractions.Controls;

namespace NovelSpeaker.App.Features.Rules.Tts;

public partial class TtsRulesPage : System.Windows.Controls.Page, INavigationAware, INavigableView<TtsRulesViewModel>
{
    private readonly PageActivationController _activation = new();
    private readonly INavigationGuardService _navigationGuardService;
    private readonly PageEventOperationRunner _eventOperations;

    public TtsRulesPage(
        TtsRulesViewModel viewModel,
        INavigationGuardService navigationGuardService,
        PageEventOperationRunner eventOperations)
        : this()
    {
        ViewModel = viewModel;
        _navigationGuardService = navigationGuardService;
        _eventOperations = eventOperations;
        DataContext = ViewModel;
    }

    internal TtsRulesPage()
    {
        ViewModel = null!;
        _navigationGuardService = null!;
        _eventOperations = PageEventOperationRunner.DesignTime;
        InitializeComponent();
    }

    public TtsRulesViewModel ViewModel { get; }

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

    public Task OnNavigatedFromAsync()
    {
        _activation.Deactivate();
        return Task.CompletedTask;
    }

    private async void ImportRuleFileButton_OnClick(object sender, RoutedEventArgs e)
    {
        await RunEventOperationAsync("导入规则失败", ViewModel.ImportRuleFileAsync);
    }

    private async void ImportRulesFromClipboardButton_OnClick(object sender, RoutedEventArgs e)
    {
        await RunEventOperationAsync("从剪贴板导入失败", ViewModel.ImportRulesFromClipboardAsync);
    }

    private Task RunEventOperationAsync(
        string failureTitle,
        Func<CancellationToken, Task> operation)
    {
        return _eventOperations.RunAsync(_activation, failureTitle, operation);
    }
}
