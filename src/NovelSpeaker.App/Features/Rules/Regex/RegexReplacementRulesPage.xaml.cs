using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using NovelSpeaker.App.Shared.Presentation.Selection;
using System.Windows;
using NovelSpeaker.App.Shell.Activation;
using NovelSpeaker.App.Shell.Navigation;
using Wpf.Ui.Abstractions.Controls;

namespace NovelSpeaker.App.Features.Rules.Regex;

public partial class RegexReplacementRulesPage : System.Windows.Controls.Page, INavigationAware, INavigableView<RegexReplacementRulesViewModel>
{
    public ICommand SelectionCommand { get; }

    private readonly PageActivationController _activation = new();
    private readonly INavigationGuardService _navigationGuardService;
    private readonly PageEventOperationRunner _eventOperations;

    public RegexReplacementRulesPage(
        RegexReplacementRulesViewModel viewModel,
        INavigationGuardService navigationGuardService,
        PageEventOperationRunner eventOperations)
        : this()
    {
        ViewModel = viewModel;
        _navigationGuardService = navigationGuardService;
        _eventOperations = eventOperations;
        DataContext = viewModel;
    }

    internal RegexReplacementRulesPage()
    {
        ViewModel = null!;
        _navigationGuardService = null!;
        _eventOperations = PageEventOperationRunner.DesignTime;
        SelectionCommand = new AsyncRelayCommand<RegexReplacementRuleListItemViewModel>(SelectWithModifiersAsync);
        InitializeComponent();
    }

    public RegexReplacementRulesViewModel ViewModel { get; }

    private Task SelectWithModifiersAsync(RegexReplacementRuleListItemViewModel? item)
    {
        var modifiers = DesktopSelectionInput.ReadModifiers();
        return _eventOperations.RunAsync(_activation, "选择正则替换规则失败",
            token => ViewModel.SelectRuleWithModifiersAsync(item, modifiers, token));
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

    public Task OnNavigatedFromAsync()
    {
        _activation.Deactivate();
        return Task.CompletedTask;
    }

    private async void ImportRuleFileButton_OnClick(object sender, RoutedEventArgs e)
    {
        await _eventOperations.RunAsync(
            _activation,
            "导入正则替换规则失败",
            ViewModel.ImportRuleFileAsync);
    }

    private async void ImportRulesFromClipboardButton_OnClick(object sender, RoutedEventArgs e)
    {
        await _eventOperations.RunAsync(
            _activation,
            "从剪贴板导入正则替换规则失败",
            ViewModel.ImportRulesFromClipboardAsync);
    }

    private void ManagementItem_OnPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is System.Windows.FrameworkElement { DataContext: RegexReplacementRuleListItemViewModel item })
            ViewModel.HandleRuleRightClick(item);
    }

    private void ManagementItem_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Apps || e.Key == Key.F10 && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            if (sender is System.Windows.FrameworkElement { DataContext: RegexReplacementRuleListItemViewModel item })
                ViewModel.HandleRuleRightClick(item);
    }

}
