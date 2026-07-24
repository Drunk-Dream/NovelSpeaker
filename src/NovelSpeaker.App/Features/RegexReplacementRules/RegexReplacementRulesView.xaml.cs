namespace NovelSpeaker.App.Features.RegexReplacementRules;

public partial class RegexReplacementRulesView : System.Windows.Controls.UserControl
{
    private System.Windows.Point _dragStartPoint;
    private RegexReplacementRuleListItemViewModel? _dragSourceRule;

    public RegexReplacementRulesView() => InitializeComponent();

    private void DragHandle_OnPreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(this);
        _dragSourceRule = (sender as System.Windows.FrameworkElement)?.Tag as RegexReplacementRuleListItemViewModel;
    }

    private void DragHandle_OnPreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != System.Windows.Input.MouseButtonState.Pressed || _dragSourceRule is null)
        {
            return;
        }

        var position = e.GetPosition(this);
        if (System.Math.Abs(position.X - _dragStartPoint.X) < System.Windows.SystemParameters.MinimumHorizontalDragDistance &&
            System.Math.Abs(position.Y - _dragStartPoint.Y) < System.Windows.SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        System.Windows.DragDrop.DoDragDrop((System.Windows.DependencyObject)sender, _dragSourceRule, System.Windows.DragDropEffects.Move);
    }

    private void RuleItem_OnDragEnter(object sender, System.Windows.DragEventArgs e)
    {
        if (DataContext is not RegexReplacementRulesViewModel ||
            e.Data.GetData(typeof(RegexReplacementRuleListItemViewModel)) is not RegexReplacementRuleListItemViewModel)
        {
            return;
        }

        e.Effects = System.Windows.DragDropEffects.Move;
    }

    private void RuleItem_OnDragLeave(object sender, System.Windows.DragEventArgs e) { }

    private void RuleItem_OnDragOver(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(RegexReplacementRuleListItemViewModel))
            ? System.Windows.DragDropEffects.Move
            : System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private async void RuleItem_OnDrop(object sender, System.Windows.DragEventArgs e)
    {
        if (DataContext is not RegexReplacementRulesViewModel viewModel ||
            e.Data.GetData(typeof(RegexReplacementRuleListItemViewModel)) is not RegexReplacementRuleListItemViewModel source)
        {
            return;
        }

        var target = (sender as System.Windows.FrameworkElement)?.DataContext as RegexReplacementRuleListItemViewModel;
        await viewModel.ReorderByDropAsync(source, target, CancellationToken.None);
    }
}
