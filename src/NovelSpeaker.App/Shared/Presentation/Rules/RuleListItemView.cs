using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace NovelSpeaker.App.Shared.Presentation.Rules;

[TemplatePart(Name = ToggleButtonPartName, Type = typeof(ButtonBase))]
public sealed class RuleListItemView : ListDragItemView
{
    private const string ToggleButtonPartName = "PART_ToggleButton";
    static RuleListItemView()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(RuleListItemView),
            new FrameworkPropertyMetadata(typeof(RuleListItemView)));
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

    public static readonly DependencyProperty ErrorMessageProperty = DependencyProperty.Register(
        nameof(ErrorMessage),
        typeof(string),
        typeof(RuleListItemView),
        new FrameworkPropertyMetadata(string.Empty));

    public static readonly DependencyProperty HasErrorProperty = DependencyProperty.Register(
        nameof(HasError),
        typeof(bool),
        typeof(RuleListItemView),
        new FrameworkPropertyMetadata(false));

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

    public static readonly DependencyProperty CanToggleProperty = DependencyProperty.Register(
        nameof(CanToggle),
        typeof(bool),
        typeof(RuleListItemView),
        new FrameworkPropertyMetadata(true));

    public static readonly DependencyProperty SelectCommandProperty = RegisterCommand(nameof(SelectCommand));
    public static readonly DependencyProperty ToggleEnabledCommandProperty = RegisterCommand(nameof(ToggleEnabledCommand));
    public static readonly DependencyProperty ExportCommandProperty = RegisterCommand(nameof(ExportCommand));
    public static readonly DependencyProperty CopyCommandProperty = RegisterCommand(nameof(CopyCommand));
    public static readonly DependencyProperty DeleteCommandProperty = RegisterCommand(nameof(DeleteCommand));
    public static readonly DependencyProperty MoveUpCommandProperty = RegisterCommand(nameof(MoveUpCommand));
    public static readonly DependencyProperty MoveDownCommandProperty = RegisterCommand(nameof(MoveDownCommand));

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

    public string ErrorMessage
    {
        get => (string)GetValue(ErrorMessageProperty);
        set => SetValue(ErrorMessageProperty, value);
    }

    public bool HasError
    {
        get => (bool)GetValue(HasErrorProperty);
        set => SetValue(HasErrorProperty, value);
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

    public bool CanToggle
    {
        get => (bool)GetValue(CanToggleProperty);
        set => SetValue(CanToggleProperty, value);
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

    protected override bool IsDragExcluded(DependencyObject? source)
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

}
