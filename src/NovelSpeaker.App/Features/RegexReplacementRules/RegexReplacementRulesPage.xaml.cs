using System.Windows;
using System.Windows.Input;
using NovelSpeaker.App.Shell.Activation;
using NovelSpeaker.App.Shell.Navigation;
using Wpf.Ui.Abstractions.Controls;

namespace NovelSpeaker.App.Features.RegexReplacementRules;

public partial class RegexReplacementRulesPage : System.Windows.Controls.Page, INavigationAware, INavigableView<RegexReplacementRulesViewModel>
{
    private readonly PageActivationController _activation = new();
    private readonly INavigationGuardService _navigationGuardService;
    private readonly PageEventOperationRunner _eventOperations;
    private Point _dragStartPoint;
    private RegexReplacementRuleListItemViewModel? _dragSourceRule;

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

    private void DragHandle_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(this);
        _dragSourceRule = (sender as FrameworkElement)?.Tag as RegexReplacementRuleListItemViewModel;
    }

    private void DragHandle_OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragSourceRule is null)
        {
            return;
        }

        var position = e.GetPosition(this);
        if (Math.Abs(position.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        DragDrop.DoDragDrop((DependencyObject)sender, _dragSourceRule, DragDropEffects.Move);
    }

    private void RuleItem_OnDragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(RegexReplacementRuleListItemViewModel)) is RegexReplacementRuleListItemViewModel)
        {
            e.Effects = DragDropEffects.Move;
        }
    }

    private void RuleItem_OnDragLeave(object sender, DragEventArgs e)
    {
    }

    private void RuleItem_OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(RegexReplacementRuleListItemViewModel))
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void RuleItem_OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(RegexReplacementRuleListItemViewModel)) is not RegexReplacementRuleListItemViewModel source)
        {
            return;
        }

        var target = (sender as FrameworkElement)?.DataContext as RegexReplacementRuleListItemViewModel;
        await _eventOperations.RunAsync(
            _activation,
            "调整替换规则顺序失败",
            cancellationToken => ViewModel.ReorderByDropAsync(source, target, cancellationToken));
    }
}
