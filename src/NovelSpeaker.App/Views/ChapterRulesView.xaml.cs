using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NovelSpeaker.App.ViewModels;

namespace NovelSpeaker.App.Views;

public partial class ChapterRulesView : UserControl
{
    private Point _dragStartPoint;
    private ChapterRuleListItemViewModel? _dragSourceRule;

    public ChapterRulesView()
    {
        InitializeComponent();
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
        if (DataContext is not ChapterRulesViewModel viewModel ||
            e.Data.GetData(typeof(ChapterRuleListItemViewModel)) is not ChapterRuleListItemViewModel)
        {
            return;
        }

        viewModel.SetDragTarget((sender as FrameworkElement)?.DataContext as ChapterRuleListItemViewModel);
    }

    private void RuleItem_OnDragLeave(object sender, DragEventArgs e)
    {
        if (DataContext is ChapterRulesViewModel viewModel)
        {
            viewModel.ClearDragTarget();
        }
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
        if (DataContext is not ChapterRulesViewModel viewModel ||
            e.Data.GetData(typeof(ChapterRuleListItemViewModel)) is not ChapterRuleListItemViewModel sourceRule)
        {
            return;
        }

        var targetRule = (sender as FrameworkElement)?.DataContext as ChapterRuleListItemViewModel;
        await viewModel.ReorderByDropAsync(sourceRule, targetRule, CancellationToken.None);
    }
}
