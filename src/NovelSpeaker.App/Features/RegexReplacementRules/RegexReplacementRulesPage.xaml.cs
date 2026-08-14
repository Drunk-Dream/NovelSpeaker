using System.Windows;
using NovelSpeaker.App.Shell.Activation;
using NovelSpeaker.App.Shell.Navigation;
using Wpf.Ui.Abstractions.Controls;

namespace NovelSpeaker.App.Features.RegexReplacementRules;

public partial class RegexReplacementRulesPage : System.Windows.Controls.Page, INavigationAware, INavigableView<RegexReplacementRulesViewModel>
{
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
        InitializeComponent();
    }

    public RegexReplacementRulesViewModel ViewModel { get; }

    public async Task OnNavigatedToAsync()
    {
        var activation = _activation.Activate();
        activation.Register(ViewModel.HandleNavigatedFrom);
        activation.Register(_navigationGuardService.Register(ViewModel.ConfirmLeaveAsync));
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

}
