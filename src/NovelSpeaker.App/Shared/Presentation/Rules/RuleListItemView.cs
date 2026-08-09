using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace NovelSpeaker.App.Shared.Presentation.Rules;

[TemplatePart(Name = ToggleButtonPartName, Type = typeof(ButtonBase))]
public sealed class RuleListItemView : Control
{
    private const string ToggleButtonPartName = "PART_ToggleButton";
    private const double AutoScrollEdgeSize = 32;
    private readonly RuleDragGestureStateMachine _dragGesture = new(
        minimumHorizontalDistance: SystemParameters.MinimumHorizontalDragDistance,
        minimumVerticalDistance: SystemParameters.MinimumVerticalDragDistance);

    static RuleListItemView()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(RuleListItemView),
            new FrameworkPropertyMetadata(typeof(RuleListItemView)));
    }

    public RuleListItemView()
    {
        Focusable = false;
        IsTabStop = false;
        AllowDrop = true;
    }

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title),
        typeof(string),
        typeof(RuleListItemView),
        new FrameworkPropertyMetadata(string.Empty, OnTitleChanged));

    public static readonly DependencyProperty SummaryProperty = DependencyProperty.Register(
        nameof(Summary),
        typeof(string),
        typeof(RuleListItemView),
        new FrameworkPropertyMetadata(string.Empty));

    public static readonly DependencyProperty IsRuleEnabledProperty = DependencyProperty.Register(
        nameof(IsRuleEnabled),
        typeof(bool),
        typeof(RuleListItemView),
        new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty IsSelectedProperty = DependencyProperty.Register(
        nameof(IsSelected),
        typeof(bool),
        typeof(RuleListItemView),
        new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty IsSortableProperty = DependencyProperty.Register(
        nameof(IsSortable),
        typeof(bool),
        typeof(RuleListItemView),
        new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty IsDraggingProperty = DependencyProperty.Register(
        nameof(IsDragging),
        typeof(bool),
        typeof(RuleListItemView),
        new FrameworkPropertyMetadata(false));

    public static readonly DependencyProperty DropPlacementProperty = DependencyProperty.Register(
        nameof(DropPlacement),
        typeof(RuleDropPlacement),
        typeof(RuleListItemView),
        new FrameworkPropertyMetadata(RuleDropPlacement.None));

    public static readonly DependencyProperty SelectCommandProperty = RegisterCommand(nameof(SelectCommand));
    public static readonly DependencyProperty ToggleEnabledCommandProperty = RegisterCommand(nameof(ToggleEnabledCommand));
    public static readonly DependencyProperty ExportCommandProperty = RegisterCommand(nameof(ExportCommand));
    public static readonly DependencyProperty CopyCommandProperty = RegisterCommand(nameof(CopyCommand));
    public static readonly DependencyProperty DeleteCommandProperty = RegisterCommand(nameof(DeleteCommand));
    public static readonly DependencyProperty MoveUpCommandProperty = RegisterCommand(nameof(MoveUpCommand));
    public static readonly DependencyProperty MoveDownCommandProperty = RegisterCommand(nameof(MoveDownCommand));
    public static readonly DependencyProperty ReorderCommandProperty = RegisterCommand(nameof(ReorderCommand));

    public static readonly DependencyProperty CommandParameterProperty = DependencyProperty.Register(
        nameof(CommandParameter),
        typeof(object),
        typeof(RuleListItemView),
        new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty CanExportProperty = RegisterCapability(nameof(CanExport), true);
    public static readonly DependencyProperty CanCopyProperty = RegisterCapability(nameof(CanCopy), true);
    public static readonly DependencyProperty CanDeleteProperty = RegisterCapability(nameof(CanDelete), true);
    public static readonly DependencyProperty CanMoveUpProperty = RegisterCapability(nameof(CanMoveUp), true);
    public static readonly DependencyProperty CanMoveDownProperty = RegisterCapability(nameof(CanMoveDown), true);

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Summary
    {
        get => (string)GetValue(SummaryProperty);
        set => SetValue(SummaryProperty, value);
    }

    public bool IsRuleEnabled
    {
        get => (bool)GetValue(IsRuleEnabledProperty);
        set => SetValue(IsRuleEnabledProperty, value);
    }

    public bool IsSelected
    {
        get => (bool)GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    public bool IsSortable
    {
        get => (bool)GetValue(IsSortableProperty);
        set => SetValue(IsSortableProperty, value);
    }

    public bool IsDragging
    {
        get => (bool)GetValue(IsDraggingProperty);
        set => SetValue(IsDraggingProperty, value);
    }

    public RuleDropPlacement DropPlacement
    {
        get => (RuleDropPlacement)GetValue(DropPlacementProperty);
        set => SetValue(DropPlacementProperty, value);
    }

    public ICommand? SelectCommand
    {
        get => (ICommand?)GetValue(SelectCommandProperty);
        set => SetValue(SelectCommandProperty, value);
    }

    public ICommand? ToggleEnabledCommand
    {
        get => (ICommand?)GetValue(ToggleEnabledCommandProperty);
        set => SetValue(ToggleEnabledCommandProperty, value);
    }

    public ICommand? ExportCommand
    {
        get => (ICommand?)GetValue(ExportCommandProperty);
        set => SetValue(ExportCommandProperty, value);
    }

    public ICommand? CopyCommand
    {
        get => (ICommand?)GetValue(CopyCommandProperty);
        set => SetValue(CopyCommandProperty, value);
    }

    public ICommand? DeleteCommand
    {
        get => (ICommand?)GetValue(DeleteCommandProperty);
        set => SetValue(DeleteCommandProperty, value);
    }

    public ICommand? MoveUpCommand
    {
        get => (ICommand?)GetValue(MoveUpCommandProperty);
        set => SetValue(MoveUpCommandProperty, value);
    }

    public ICommand? MoveDownCommand
    {
        get => (ICommand?)GetValue(MoveDownCommandProperty);
        set => SetValue(MoveDownCommandProperty, value);
    }

    public ICommand? ReorderCommand
    {
        get => (ICommand?)GetValue(ReorderCommandProperty);
        set => SetValue(ReorderCommandProperty, value);
    }

    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    public bool CanExport
    {
        get => (bool)GetValue(CanExportProperty);
        set => SetValue(CanExportProperty, value);
    }

    public bool CanCopy
    {
        get => (bool)GetValue(CanCopyProperty);
        set => SetValue(CanCopyProperty, value);
    }

    public bool CanDelete
    {
        get => (bool)GetValue(CanDeleteProperty);
        set => SetValue(CanDeleteProperty, value);
    }

    public bool CanMoveUp
    {
        get => (bool)GetValue(CanMoveUpProperty);
        set => SetValue(CanMoveUpProperty, value);
    }

    public bool CanMoveDown
    {
        get => (bool)GetValue(CanMoveDownProperty);
        set => SetValue(CanMoveDownProperty, value);
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        UpdateToggleAutomationName();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (TryOpenContextMenuFromKeyboard(e.Key, Keyboard.Modifiers))
        {
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonDown(e);
        if (!IsSortable)
        {
            return;
        }

        _dragGesture.Press(
            e.GetPosition(this),
            e.Timestamp,
            IsWithinToggle(e.OriginalSource as DependencyObject));
    }

    protected override void OnPreviewMouseMove(MouseEventArgs e)
    {
        base.OnPreviewMouseMove(e);
        if (!IsSortable ||
            !_dragGesture.ShouldBeginDrag(
                e.GetPosition(this),
                e.Timestamp,
                e.LeftButton == MouseButtonState.Pressed))
        {
            return;
        }

        IsDragging = true;
        try
        {
            var source = CommandParameter ?? DataContext;
            if (source is null)
            {
                return;
            }

            DragDrop.DoDragDrop(
                this,
                new RuleDragPayload(source),
                DragDropEffects.Move);
        }
        finally
        {
            IsDragging = false;
            ClearInsertionPlacements();
        }

        e.Handled = true;
    }

    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        _dragGesture.Cancel();
        base.OnPreviewMouseLeftButtonUp(e);
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        _dragGesture.Cancel();
        base.OnLostMouseCapture(e);
    }

    protected override void OnQueryContinueDrag(QueryContinueDragEventArgs e)
    {
        if (IsDragging && FindAncestor<ScrollViewer>(this) is { } scrollViewer)
        {
            ScrollAtListEdge(scrollViewer, Mouse.GetPosition(scrollViewer).Y);
        }

        base.OnQueryContinueDrag(e);
    }

    protected override void OnDragOver(DragEventArgs e)
    {
        base.OnDragOver(e);
        if (!TryGetPayload(e, out var payload) || !IsSortable || Equals(payload.Source, CommandParameter ?? DataContext))
        {
            DropPlacement = RuleDropPlacement.None;
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        DropPlacement = RuleDragGeometry.ResolvePlacement(e.GetPosition(this).Y, ActualHeight);
        e.Effects = DropPlacement == RuleDropPlacement.None
            ? DragDropEffects.None
            : DragDropEffects.Move;
        e.Handled = true;
    }

    protected override void OnDragLeave(DragEventArgs e)
    {
        DropPlacement = RuleDropPlacement.None;
        base.OnDragLeave(e);
    }

    protected override void OnDrop(DragEventArgs e)
    {
        base.OnDrop(e);
        var placement = DropPlacement;
        DropPlacement = RuleDropPlacement.None;
        if (!TryGetPayload(e, out var payload) ||
            placement == RuleDropPlacement.None ||
            !IsSortable)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var target = CommandParameter ?? DataContext;
        if (target is null || Equals(payload.Source, target))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.Effects = CommitReorder(payload.Source, target, placement)
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    internal void ExecuteSelect() => Execute(SelectCommand, CommandParameter ?? DataContext);

    internal bool TryOpenContextMenuFromKeyboard(Key key, ModifierKeys modifiers)
    {
        if (key != Key.Apps && (key != Key.F10 || !modifiers.HasFlag(ModifierKeys.Shift)))
        {
            return false;
        }

        if (ContextMenu is null)
        {
            return false;
        }

        ContextMenu.PlacementTarget = this;
        ContextMenu.IsOpen = true;
        return true;
    }

    internal bool CommitReorder(object source, object target, RuleDropPlacement placement)
    {
        if (placement == RuleDropPlacement.None ||
            Equals(source, target) ||
            ReorderCommand?.CanExecute(new RuleReorderRequest(source, target, placement)) != true)
        {
            return false;
        }

        ReorderCommand.Execute(new RuleReorderRequest(source, target, placement));
        return true;
    }

    internal static int ResolveEdgeScrollDirection(ScrollViewer scrollViewer, double pointerY) =>
        RuleDragGeometry.ResolveEdgeScrollDirection(
            pointerY,
            scrollViewer.RenderSize.Height,
            AutoScrollEdgeSize);

    private static DependencyProperty RegisterCommand(string name) => DependencyProperty.Register(
        name,
        typeof(ICommand),
        typeof(RuleListItemView),
        new FrameworkPropertyMetadata(null));

    private static DependencyProperty RegisterCapability(string name, bool defaultValue) => DependencyProperty.Register(
        name,
        typeof(bool),
        typeof(RuleListItemView),
        new FrameworkPropertyMetadata(defaultValue));

    private static void OnTitleChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e) =>
        ((RuleListItemView)dependencyObject).UpdateToggleAutomationName();

    private void UpdateToggleAutomationName()
    {
        if (GetTemplateChild(ToggleButtonPartName) is FrameworkElement toggleButton)
        {
            AutomationProperties.SetName(toggleButton, $"切换规则启用状态：{Title}");
        }
    }

    private static void Execute(ICommand? command, object? parameter)
    {
        if (command?.CanExecute(parameter) == true)
        {
            command.Execute(parameter);
        }
    }

    private bool IsWithinToggle(DependencyObject? source)
    {
        var toggle = GetTemplateChild(ToggleButtonPartName) as DependencyObject;
        for (var current = source; current is not null; current = GetParent(current))
        {
            if (ReferenceEquals(current, toggle))
            {
                return true;
            }

            if (ReferenceEquals(current, this))
            {
                break;
            }
        }

        return false;
    }

    private static DependencyObject? GetParent(DependencyObject current) =>
        current is Visual or System.Windows.Media.Media3D.Visual3D
            ? VisualTreeHelper.GetParent(current)
            : LogicalTreeHelper.GetParent(current);

    private static bool TryGetPayload(DragEventArgs e, out RuleDragPayload payload)
    {
        payload = e.Data.GetData(typeof(RuleDragPayload)) as RuleDragPayload ?? null!;
        return payload is not null;
    }

    private static void ScrollAtListEdge(ScrollViewer scrollViewer, double pointerY)
    {
        var direction = ResolveEdgeScrollDirection(scrollViewer, pointerY);
        if (direction < 0)
        {
            scrollViewer.LineUp();
        }
        else if (direction > 0)
        {
            scrollViewer.LineDown();
        }
    }

    private void ClearInsertionPlacements()
    {
        var itemsControl = FindAncestor<ItemsControl>(this);
        if (itemsControl is null)
        {
            DropPlacement = RuleDropPlacement.None;
            return;
        }

        foreach (var view in FindDescendants<RuleListItemView>(itemsControl))
        {
            view.DropPlacement = RuleDropPlacement.None;
        }
    }

    private static T? FindAncestor<T>(DependencyObject start) where T : DependencyObject
    {
        for (var current = GetParent(start); current is not null; current = GetParent(current))
        {
            if (current is T match)
            {
                return match;
            }
        }

        return null;
    }

    private static IEnumerable<T> FindDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in FindDescendants<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private sealed record RuleDragPayload(object Source);
}
