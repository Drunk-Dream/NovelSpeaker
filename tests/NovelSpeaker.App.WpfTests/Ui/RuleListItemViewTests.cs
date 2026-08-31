using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Xml.Linq;
using Xunit;
using Button = System.Windows.Controls.Button;
using MenuItem = System.Windows.Controls.MenuItem;

namespace NovelSpeaker.App.WpfTests.Ui;

[Collection("WpfDispatcher")]
public sealed class RuleListItemViewTests
{
    private void Shared_view_consumes_display_state_and_commands_without_rule_view_model_types()
    {
        WpfTestHost.RunInSta(() =>
        {
            var item = new RuleItemContract("示例规则", "POST · speech.example", true, true);
            var selected = new RecordingCommand();
            var toggled = new RecordingCommand();
            var view = CreateBoundView(item, selected, toggled);
            using var host = Show(view);

            Assert.Equal(item.Title, view.Title);
            Assert.Equal(item.Summary, view.Summary);
            Assert.True(view.IsRuleEnabled);
            Assert.True(view.IsSelected);

            var buttons = VisualTreeTestHelper.FindDescendants<Button>(view).ToArray();
            var selectionButton = Assert.Single(
                buttons,
                button => AutomationProperties.GetName(button) == item.Title);
            var toggle = Assert.Single(VisualTreeTestHelper.FindDescendants<RuleToggleSwitch>(view));
            var surface = Assert.IsType<Border>(view.Template.FindName("Surface", view));

            Assert.Same(view.FindResource("App.Selection.RuleCard"), surface.Style);
            Assert.Same(view.FindResource("App.Button.Floating"), selectionButton.Style);
            Assert.Equal(
                $"切换规则启用状态：{item.Title}",
                AutomationProperties.GetName(toggle));
            Assert.True(toggle.Focusable);
            Assert.True(toggle.IsTabStop);
            Assert.True(toggle.IsChecked);
            var toggleCommand = Assert.IsAssignableFrom<ICommand>(toggle.Command);
            Assert.True(toggleCommand.CanExecute(toggle.CommandParameter));
            toggleCommand.Execute(toggle.CommandParameter);
            Assert.Equal([item], toggled.Parameters);
            Assert.Empty(selected.Parameters);

            Assert.True(selectionButton.Command.CanExecute(selectionButton.CommandParameter));
            selectionButton.Command.Execute(selectionButton.CommandParameter);
            Assert.Equal([item], selected.Parameters);
            Assert.Equal(40, toggle.ActualWidth);
            Assert.True(toggle.ActualWidth < view.ActualWidth / 2);
            Assert.False(view.Focusable);
            Assert.False(view.IsTabStop);
            Assert.Equal(
                2,
                VisualTreeTestHelper.FindDescendants<Control>(view).Count(control => control.IsTabStop));
        });
    }

    private void Right_click_does_not_select_and_context_menu_exposes_capability_state()
    {
        WpfTestHost.RunInSta(() =>
        {
            var item = new RuleItemContract("章节规则", "^第.+章$", true, false);
            var selected = new RecordingCommand();
            var view = CreateBoundView(item, selected, new RecordingCommand());
            view.IsSortable = true;
            view.CanMoveUp = false;
            view.CanMoveDown = true;
            view.CanDelete = false;
            using var host = Show(view);

            view.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Right)
            {
                RoutedEvent = UIElement.MouseRightButtonUpEvent
            });

            Assert.Empty(selected.Parameters);
            Assert.False(view.Focusable);
            Assert.False(view.IsTabStop);
            var menu = Assert.IsType<ContextMenu>(view.ContextMenu);
            menu.PlacementTarget = view;
            menu.IsOpen = true;
            menu.UpdateLayout();

            var items = menu.Items.OfType<MenuItem>().ToDictionary(item => (string)item.Header);
            Assert.Equal(
                ["导出到文件", "复制到剪切板", "上移", "下移", "删除"],
                items.Keys);
            Assert.False(items["上移"].IsEnabled);
            Assert.True(items["下移"].IsEnabled);
            Assert.False(items["删除"].IsEnabled);
            menu.IsOpen = false;
        });
    }

    private void Non_sortable_menu_omits_move_fallbacks_and_remains_keyboard_focusable()
    {
        WpfTestHost.RunInSta(() =>
        {
            var view = CreateBoundView(
                new RuleItemContract("TTS", "GET · speech.example", true, false),
                new RecordingCommand(),
                new RecordingCommand());
            using var host = Show(view);

            var menu = Assert.IsType<ContextMenu>(view.ContextMenu);
            var selectionButton = Assert.Single(
                VisualTreeTestHelper.FindDescendants<Button>(view),
                button => AutomationProperties.GetName(button) == view.Title);
            selectionButton.RaiseEvent(new KeyEventArgs(
                Keyboard.PrimaryDevice,
                PresentationSource.FromVisual(selectionButton),
                0,
                Key.Apps)
            {
                RoutedEvent = Keyboard.KeyDownEvent
            });
            Assert.True(menu.IsOpen);
            menu.UpdateLayout();

            var visibleHeaders = menu.Items
                .OfType<MenuItem>()
                .Where(item => item.Visibility == Visibility.Visible)
                .Select(item => (string)item.Header)
                .ToArray();
            Assert.Equal(["导出到文件", "复制到剪切板", "删除"], visibleHeaders);
            Assert.Equal("规则操作", AutomationProperties.GetName(menu));
            Assert.False(view.Focusable);
            menu.IsOpen = false;

            Assert.True(view.TryOpenContextMenuFromKeyboard(Key.F10, ModifierKeys.Shift));
            Assert.Same(view, menu.PlacementTarget);
            menu.IsOpen = false;
            Assert.False(view.TryOpenContextMenuFromKeyboard(Key.F10, ModifierKeys.None));
        });
    }

    private void Recycled_view_refreshes_all_visual_state_from_the_new_contract()
    {
        WpfTestHost.RunInSta(() =>
        {
            var first = new RuleItemContract("第一条", "摘要一", true, true);
            var second = new RuleItemContract("第二条", "摘要二", false, false);
            var view = CreateBoundView(first, new RecordingCommand(), new RecordingCommand());
            using var host = Show(view);

            view.DataContext = second;
            view.CommandParameter = second;
            host.Window.UpdateLayout();

            Assert.Equal("第二条", view.Title);
            Assert.Equal("摘要二", view.Summary);
            Assert.False(view.IsRuleEnabled);
            Assert.False(view.IsSelected);
            var toggle = Assert.Single(VisualTreeTestHelper.FindDescendants<RuleToggleSwitch>(view));
            Assert.False(toggle.IsChecked);
            Assert.Equal("切换规则启用状态：第二条", AutomationProperties.GetName(toggle));
        });
    }

    private void Optional_runtime_error_uses_formal_inline_feedback_without_changing_card_commands()
    {
        WpfTestHost.RunInSta(() =>
        {
            var selected = new RecordingCommand();
            var view = new RuleListItemView
            {
                Title = "错误规则",
                Summary = "[",
                HasError = true,
                ErrorMessage = "正则表达式无效",
                SelectCommand = selected,
                CommandParameter = "rule-id",
                Width = 360
            };
            using var host = Show(view);

            var message = Assert.Single(
                VisualTreeTestHelper.FindDescendants<TextBlock>(view),
                textBlock => textBlock.Text == "正则表达式无效");
            var feedback = Assert.IsType<Border>(message.Parent);
            Assert.Equal(Visibility.Visible, feedback.Visibility);
            Assert.Same(view.FindResource("App.Feedback.InlineMessage"), feedback.Style.BasedOn);

            view.HasError = false;
            host.Window.UpdateLayout();
            Assert.Equal(Visibility.Collapsed, feedback.Visibility);
            Assert.Empty(selected.Parameters);
        });
    }

    private void Reorder_command_runs_only_for_a_valid_drop_commit()
    {
        WpfTestHost.RunInSta(() =>
        {
            var command = new RecordingCommand();
            var view = new RuleListItemView { ReorderCommand = command };
            var source = new object();
            var target = new object();

            Assert.False(view.CommitReorder(source, target, RuleDropPlacement.None));
            Assert.False(view.CommitReorder(source, source, RuleDropPlacement.Before));
            Assert.Empty(command.Parameters);

            Assert.True(view.CommitReorder(source, target, RuleDropPlacement.After));
            var request = Assert.IsType<RuleReorderRequest>(Assert.Single(command.Parameters));
            Assert.Same(source, request.Source);
            Assert.Same(target, request.Target);
            Assert.Equal(RuleDropPlacement.After, request.Placement);
        });
    }

    private void Toggle_automation_uses_the_same_single_command_path_and_reprojects_state()
    {
        WpfTestHost.RunInSta(() =>
        {
            RuleListItemView view = null!;
            var command = new RecordingCommand(_ => view.IsRuleEnabled = false);
            view = new RuleListItemView
            {
                Title = "自动化规则",
                Summary = "自动化切换合同",
                IsRuleEnabled = true,
                CommandParameter = "rule-id",
                ToggleEnabledCommand = command,
                Width = 360
            };
            using var host = Show(view);
            var toggle = Assert.Single(VisualTreeTestHelper.FindDescendants<RuleToggleSwitch>(view));
            var peer = Assert.IsType<RuleToggleSwitchAutomationPeer>(
                UIElementAutomationPeer.CreatePeerForElement(toggle));
            var provider = Assert.IsAssignableFrom<IToggleProvider>(peer.GetPattern(PatternInterface.Toggle));

            provider.Toggle();

            Assert.Equal(["rule-id"], command.Parameters);
            Assert.False(view.IsRuleEnabled);
            Assert.False(toggle.IsChecked);
        });
    }

    private void Toggle_tracks_standard_command_availability_for_input_and_automation()
    {
        WpfTestHost.RunInSta(() =>
        {
            var command = new MutableCommand();
            var view = new RuleListItemView
            {
                Title = "可用状态规则",
                IsRuleEnabled = true,
                CommandParameter = "rule-id",
                ToggleEnabledCommand = command,
                Width = 360
            };
            using var host = Show(view);
            var toggle = Assert.Single(VisualTreeTestHelper.FindDescendants<RuleToggleSwitch>(view));
            var peer = Assert.IsType<RuleToggleSwitchAutomationPeer>(
                UIElementAutomationPeer.CreatePeerForElement(toggle));
            var provider = Assert.IsAssignableFrom<IToggleProvider>(peer.GetPattern(PatternInterface.Toggle));
            Assert.True(toggle.IsEnabled);

            command.SetCanExecute(false);

            Assert.False(toggle.IsEnabled);
            Assert.Throws<ElementNotEnabledException>(provider.Toggle);
            Assert.Empty(command.Parameters);

            command.SetCanExecute(true);

            Assert.True(toggle.IsEnabled);
            provider.Toggle();
            Assert.Equal(["rule-id"], command.Parameters);
        });
    }

    private void Toggle_capability_disables_input_without_changing_projected_state()
    {
        WpfTestHost.RunInSta(() =>
        {
            var command = new RecordingCommand();
            var view = new RuleListItemView
            {
                Title = "忙碌中的规则",
                IsRuleEnabled = true,
                CanToggle = false,
                ToggleEnabledCommand = command,
                CommandParameter = "rule-id",
                Width = 360
            };
            using var host = Show(view);
            var toggle = Assert.Single(VisualTreeTestHelper.FindDescendants<RuleToggleSwitch>(view));

            Assert.False(toggle.IsEnabled);
            Assert.True(toggle.IsChecked);
            Assert.Empty(command.Parameters);

            view.CanToggle = true;
            host.Window.UpdateLayout();
            Assert.True(toggle.IsEnabled);
            Assert.True(toggle.IsChecked);
        });
    }

    private void Rule_selection_style_has_an_explicit_selected_hover_combination()
    {
        var repositoryRoot = LocateRepositoryRoot();
        var document = XDocument.Load(Path.Combine(
            repositoryRoot,
            "src",
            "NovelSpeaker.App",
            "Shared",
            "Theming",
            "Resources",
            "Styles",
            "Selection.xaml"));
        var xaml = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
        var style = document.Root!.Elements().Single(element =>
            (string?)element.Attribute(xaml + "Key") == "App.Selection.RuleCard");

        Assert.Equal("{StaticResource App.Selection.CardItem}", (string?)style.Attribute("BasedOn"));
        Assert.Single(style.Descendants(), element => element.Name.LocalName == "MultiDataTrigger");
        Assert.Contains(
            style.Descendants().Where(element => element.Name.LocalName == "Setter"),
            setter => (string?)setter.Attribute("Value") == "{DynamicResource App.Brush.Accent.Hover}");
    }

    [Fact]
    public void Rule_list_item_projection_contracts_cover_display_recycling_and_selection_style()
    {
        Shared_view_consumes_display_state_and_commands_without_rule_view_model_types();
        Recycled_view_refreshes_all_visual_state_from_the_new_contract();
        Optional_runtime_error_uses_formal_inline_feedback_without_changing_card_commands();
        Rule_selection_style_has_an_explicit_selected_hover_combination();
    }

    [Fact]
    public void Rule_list_item_interaction_contracts_cover_context_actions_and_command_capabilities()
    {
        Right_click_does_not_select_and_context_menu_exposes_capability_state();
        Non_sortable_menu_omits_move_fallbacks_and_remains_keyboard_focusable();
        Reorder_command_runs_only_for_a_valid_drop_commit();
        Toggle_automation_uses_the_same_single_command_path_and_reprojects_state();
        Toggle_tracks_standard_command_availability_for_input_and_automation();
        Toggle_capability_disables_input_without_changing_projected_state();
    }

    private static RuleListItemView CreateBoundView(
        RuleItemContract item,
        ICommand selectCommand,
        ICommand toggleCommand)
    {
        var view = new RuleListItemView
        {
            DataContext = item,
            CommandParameter = item,
            SelectCommand = selectCommand,
            ToggleEnabledCommand = toggleCommand,
            Width = 360
        };
        view.SetBinding(RuleListItemView.TitleProperty, new Binding(nameof(RuleItemContract.Title)));
        view.SetBinding(RuleListItemView.SummaryProperty, new Binding(nameof(RuleItemContract.Summary)));
        view.SetBinding(RuleListItemView.IsRuleEnabledProperty, new Binding(nameof(RuleItemContract.IsEnabled)));
        view.SetBinding(RuleListItemView.IsSelectedProperty, new Binding(nameof(RuleItemContract.IsSelected)));
        return view;
    }

    private static WpfWindowHost Show(FrameworkElement view)
    {
        var host = WpfWindowHost.Show(new Window
        {
            Content = view,
            Width = 420,
            Height = 180,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.ToolWindow
        });
        host.Window.UpdateLayout();
        return host;
    }

    private static string LocateRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "NovelSpeaker.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the NovelSpeaker repository root.");
    }

    private sealed record RuleItemContract(
        string Title,
        string Summary,
        bool IsEnabled,
        bool IsSelected);

    private sealed class RecordingCommand(Action<object?>? execute = null) : ICommand
    {
        public List<object?> Parameters { get; } = [];

        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter)
        {
            Parameters.Add(parameter);
            execute?.Invoke(parameter);
        }
    }

    private sealed class MutableCommand : ICommand
    {
        private bool _canExecute = true;

        public List<object?> Parameters { get; } = [];

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => _canExecute;

        public void Execute(object? parameter) => Parameters.Add(parameter);

        public void SetCanExecute(bool value)
        {
            _canExecute = value;
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
