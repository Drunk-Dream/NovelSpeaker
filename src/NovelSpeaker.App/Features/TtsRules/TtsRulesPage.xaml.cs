using System.Windows;
using System.Windows.Controls;
using NovelSpeaker.App.Shell.Activation;
using NovelSpeaker.App.Shell.Navigation;
using Wpf.Ui.Abstractions.Controls;

namespace NovelSpeaker.App.Features.TtsRules;

public partial class TtsRulesPage : System.Windows.Controls.Page, INavigationAware, INavigableView<TtsRulesViewModel>
{
    private readonly PageActivationController _activation = new();
    private readonly INavigationGuardService _navigationGuardService;
    private readonly PageEventOperationRunner _eventOperations;
    private bool _hasLoaded;

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
        var activation = _activation.Activate();
        activation.Register(ViewModel.HandleNavigatedFrom);
        activation.Register(_navigationGuardService.Register(ViewModel.ConfirmLeaveAsync));

        if (_hasLoaded)
        {
            return;
        }

        try
        {
            await ViewModel.LoadAsync(activation.CancellationToken);
            activation.TryCommit(() => _hasLoaded = true);
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

    private async void ImportRuleFileButton_OnClick(object sender, RoutedEventArgs e)
    {
        await RunEventOperationAsync("导入规则失败", ViewModel.ImportRuleFileAsync);
    }

    private async void ImportRulesFromClipboardButton_OnClick(object sender, RoutedEventArgs e)
    {
        await RunEventOperationAsync("从剪贴板导入失败", ViewModel.ImportRulesFromClipboardAsync);
    }

    private void RuleMoreButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button ||
            button.ContextMenu is null ||
            button.DataContext is not TtsRuleListItemViewModel rule)
        {
            return;
        }

        button.ContextMenu.DataContext = rule;
        button.ContextMenu.PlacementTarget = button;
        button.ContextMenu.IsOpen = true;
    }

    private async void ExportRuleMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: TtsRuleListItemViewModel rule })
        {
            return;
        }

        await RunEventOperationAsync(
            "导出规则失败",
            cancellationToken => ViewModel.ExportRuleAsync(rule, cancellationToken));
    }

    private async void CopyRuleMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: TtsRuleListItemViewModel rule })
        {
            return;
        }

        await RunEventOperationAsync(
            "复制规则失败",
            cancellationToken => ViewModel.CopyRuleAsync(rule, cancellationToken));
    }

    private async void DeleteRuleMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: TtsRuleListItemViewModel rule })
        {
            return;
        }

        await RunEventOperationAsync(
            "删除规则失败",
            cancellationToken => ViewModel.DeleteRuleFromListAsync(rule, cancellationToken));
    }

    private Task RunEventOperationAsync(
        string failureTitle,
        Func<CancellationToken, Task> operation)
    {
        return _eventOperations.RunAsync(_activation, failureTitle, operation);
    }
}
