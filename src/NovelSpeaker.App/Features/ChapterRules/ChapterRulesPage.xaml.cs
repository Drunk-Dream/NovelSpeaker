using System.Windows;
using System.Windows.Input;
using NovelSpeaker.App.Shell.Activation;
using NovelSpeaker.App.Shell.Navigation;
using Wpf.Ui.Abstractions.Controls;

namespace NovelSpeaker.App.Features.ChapterRules;

public partial class ChapterRulesPage : System.Windows.Controls.Page, INavigationAware, INavigableView<ChapterRulesViewModel>
{
    private readonly PageActivationController _activation = new();
    private readonly INavigationGuardService _navigationGuardService;
    private readonly PageEventOperationRunner _eventOperations;
    private Point _dragStartPoint;
    private ChapterRuleListItemViewModel? _dragSourceRule;
    private bool _hasLoaded;

    public ChapterRulesPage(
        ChapterRulesViewModel viewModel,
        INavigationGuardService navigationGuardService,
        PageEventOperationRunner eventOperations)
        : this()
    {
        ViewModel = viewModel;
        _navigationGuardService = navigationGuardService;
        _eventOperations = eventOperations;
        DataContext = ViewModel;
    }

    internal ChapterRulesPage()
    {
        ViewModel = null!;
        _navigationGuardService = null!;
        _eventOperations = PageEventOperationRunner.DesignTime;
        InitializeComponent();
    }

    public ChapterRulesViewModel ViewModel { get; }

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
        await _eventOperations.RunAsync(
            _activation,
            "导入章节规则失败",
            ViewModel.ImportRuleFileAsync);
    }

    private async void ImportRulesFromClipboardButton_OnClick(object sender, RoutedEventArgs e)
    {
        await _eventOperations.RunAsync(
            _activation,
            "从剪贴板导入章节规则失败",
            ViewModel.ImportRulesFromClipboardAsync);
    }

    private void DragHandle_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(this);
        _dragSourceRule = (sender as FrameworkElement)?.Tag as ChapterRuleListItemViewModel;
    }

    private void DragHandle_OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragSourceRule is null)
        {
            return;
        }

        var currentPosition = e.GetPosition(this);
        if (Math.Abs(currentPosition.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(currentPosition.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        DragDrop.DoDragDrop((DependencyObject)sender, _dragSourceRule, DragDropEffects.Move);
    }

    private void RuleItem_OnDragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(ChapterRuleListItemViewModel)) is not ChapterRuleListItemViewModel)
        {
            return;
        }

        ViewModel.SetDragTarget((sender as FrameworkElement)?.DataContext as ChapterRuleListItemViewModel);
    }

    private void RuleItem_OnDragLeave(object sender, DragEventArgs e)
    {
        ViewModel.ClearDragTarget();
    }

    private void RuleItem_OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(ChapterRuleListItemViewModel))
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void RuleItem_OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(ChapterRuleListItemViewModel)) is not ChapterRuleListItemViewModel sourceRule)
        {
            return;
        }

        var targetRule = (sender as FrameworkElement)?.DataContext as ChapterRuleListItemViewModel;
        await _eventOperations.RunAsync(
            _activation,
            "调整章节规则顺序失败",
            cancellationToken => ViewModel.ReorderByDropAsync(sourceRule, targetRule, cancellationToken));
    }

    private void RuleMoreButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button ||
            button.ContextMenu is null ||
            button.DataContext is not ChapterRuleListItemViewModel rule)
        {
            return;
        }

        button.ContextMenu.DataContext = rule;
        foreach (var item in button.ContextMenu.Items.OfType<FrameworkElement>())
        {
            item.DataContext = rule;
        }

        button.ContextMenu.PlacementTarget = button;
        button.ContextMenu.IsOpen = true;
    }

    private async void MoveRuleUpMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.MenuItem { DataContext: ChapterRuleListItemViewModel rule })
        {
            return;
        }

        await _eventOperations.RunAsync(
            _activation,
            "调整章节规则顺序失败",
            cancellationToken => ViewModel.MoveRuleUpFromListAsync(rule, cancellationToken));
    }

    private async void ExportRuleMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.MenuItem { DataContext: ChapterRuleListItemViewModel rule })
        {
            return;
        }

        await _eventOperations.RunAsync(
            _activation,
            "导出章节规则失败",
            cancellationToken => ViewModel.ExportRuleAsync(rule, cancellationToken));
    }

    private async void CopyRuleMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.MenuItem { DataContext: ChapterRuleListItemViewModel rule })
        {
            return;
        }

        await _eventOperations.RunAsync(
            _activation,
            "复制章节规则失败",
            cancellationToken => ViewModel.CopyRuleAsync(rule, cancellationToken));
    }

    private async void MoveRuleDownMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.MenuItem { DataContext: ChapterRuleListItemViewModel rule })
        {
            return;
        }

        await _eventOperations.RunAsync(
            _activation,
            "调整章节规则顺序失败",
            cancellationToken => ViewModel.MoveRuleDownFromListAsync(rule, cancellationToken));
    }

    private async void DeleteRuleMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.MenuItem { DataContext: ChapterRuleListItemViewModel rule })
        {
            return;
        }

        await _eventOperations.RunAsync(
            _activation,
            "删除章节规则失败",
            cancellationToken => ViewModel.DeleteRuleFromListAsync(rule, cancellationToken));
    }
}
