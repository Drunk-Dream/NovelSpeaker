using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovelSpeaker.App.Shared.Presentation.Controls.Common;
using NovelSpeaker.App.Shared.Presentation.Controls.Forms;
using NovelSpeaker.App.Shared.Presentation.Rules;
using NovelSpeaker.Domain.Books;
using SymbolIcon = Wpf.Ui.Controls.SymbolIcon;
using SymbolRegular = Wpf.Ui.Controls.SymbolRegular;
using Xunit;

namespace NovelSpeaker.App.WpfTests.Ui;

[Collection("WpfDispatcher")]
public sealed partial class RegexReplacementRulesPageTests
{
    [Fact]
    public void RegexReplacementRulesPage_toolbar_has_import_and_help_actions_without_page_export()
    {
        WpfTestHost.RunInSta(() =>
        {
            var view = CreateView(new RegexReplacementRulesViewLayoutContext());

            AssertToolbarIcon(view, "新建规则", SymbolRegular.DocumentAdd24);
            AssertToolbarIcon(view, "从文件导入", SymbolRegular.ArrowImport24);
            AssertToolbarIcon(view, "从剪切板导入", SymbolRegular.ClipboardPaste24);
            AssertToolbarIcon(view, "正则替换帮助", SymbolRegular.QuestionCircle24);
            Assert.DoesNotContain(
                VisualTreeTestHelper.FindDescendants<Button>(view),
                button => AutomationProperties.GetName(button).Contains("导出", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void RegexReplacementRulesPage_keeps_rule_list_and_editor_scrollable_with_virtualization()
    {
        WpfTestHost.RunInSta(() =>
        {
            var rules = new ObservableCollection<RegexReplacementRuleListItemViewModel>();
            for (var index = 0; index < 40; index++)
            {
                rules.Add(CreateRule(
                    $"规则 {index}",
                    @"(?<=^|\s)(?:第[0-9一二三四五六七八九十百千万]+章|序章|楔子|后记)(?=\s|$)",
                    index % 2 == 0,
                    RegexReplacementScope.Both,
                    index == 0));
            }

            var view = CreateView(new RegexReplacementRulesViewLayoutContext
            {
                HasEditor = true,
                Rules = rules,
                DraftPattern = string.Join(
                    Environment.NewLine,
                    Enumerable.Repeat(rules[0].PatternSummary, 40))
            }, 900, 640);
            var list = Assert.IsType<ListBox>(view.FindName("RulesList"));
            var listScrollViewer = Assert.IsAssignableFrom<ScrollViewer>(
                VisualTreeTestHelper.FindDescendant<ScrollViewer>(list));
            var editorScrollViewer = Assert.IsType<ScrollViewer>(view.FindName("RuleEditorScrollViewer"));

            Assert.True(listScrollViewer.ScrollableHeight > 0);
            Assert.True(editorScrollViewer.ScrollableHeight > 0);
            Assert.True(VirtualizingPanel.GetIsVirtualizing(list));
            Assert.Equal(VirtualizationMode.Recycling, VirtualizingPanel.GetVirtualizationMode(list));
        });
    }

    [Fact]
    public void RegexReplacementRulesPage_context_menu_has_export_copy_move_delete_without_handles_or_ellipsis()
    {
        WpfTestHost.RunInSta(() =>
        {
            var rule = CreateRule("朗读替换", "旧文本", true, RegexReplacementScope.Speech, true);
            rule.CanMoveUp = false;
            rule.CanMoveDown = true;
            var view = CreateView(new RegexReplacementRulesViewLayoutContext { Rules = [rule] });
            var item = FindRuleItem(view, rule);
            item.ContextMenu!.PlacementTarget = item;
            item.ContextMenu.IsOpen = true;
            var menuItems = item.ContextMenu.Items.OfType<MenuItem>().ToArray();

            Assert.Equal(
                ["导出到文件", "复制到剪切板", "上移", "下移", "删除"],
                menuItems.Where(menuItem => menuItem.Visibility == Visibility.Visible).Select(menuItem => menuItem.Header));
            Assert.False(menuItems.Single(menuItem => Equals(menuItem.Header, "上移")).IsEnabled);
            Assert.True(menuItems.Single(menuItem => Equals(menuItem.Header, "下移")).IsEnabled);
            Assert.True(menuItems.Single(menuItem => Equals(menuItem.Header, "删除")).IsEnabled);
            item.ContextMenu.IsOpen = false;
            Assert.DoesNotContain(
                VisualTreeTestHelper.FindDescendants<Button>(view),
                button => AutomationProperties.GetName(button).Contains("更多操作", StringComparison.Ordinal));
            Assert.DoesNotContain(
                VisualTreeTestHelper.FindDescendants<FrameworkElement>(view),
                element => AutomationProperties.GetName(element).Contains("拖动排序", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void RegexReplacementRulesPage_busy_rule_disables_toggle_drag_and_menu_capabilities()
    {
        WpfTestHost.RunInSta(() =>
        {
            var rule = CreateRule("忙碌规则", "文本", true, RegexReplacementScope.Both, false);
            rule.CanQuickActions = false;
            rule.CanMoveUp = false;
            rule.CanMoveDown = false;
            var view = CreateView(new RegexReplacementRulesViewLayoutContext { Rules = [rule] });
            var item = FindRuleItem(view, rule);
            var toggle = Assert.Single(VisualTreeTestHelper.FindDescendants<RuleToggleSwitch>(item));

            Assert.False(item.CanToggle);
            Assert.False(item.IsSortable);
            Assert.False(toggle.IsEnabled);
            Assert.True(toggle.IsChecked);
            Assert.False(item.CanExport);
            Assert.False(item.CanCopy);
            Assert.False(item.CanDelete);
        });
    }

    [Fact]
    public void RegexReplacementRulesPage_projects_editor_and_runtime_errors_through_formal_feedback()
    {
        WpfTestHost.RunInSta(() =>
        {
            var rule = CreateRule(
                "错误规则",
                "[",
                false,
                RegexReplacementScope.Display,
                true,
                "规则执行失败");
            var view = CreateView(new RegexReplacementRulesViewLayoutContext
            {
                HasEditor = true,
                Rules = [rule],
                PatternValidationMessage = "正则表达式无效。",
                CanCancel = true,
                CanSave = false
            });

            var fields = VisualTreeTestHelper.FindDescendants<AppFormField>(view).ToArray();
            Assert.Equal(["名称", "正则表达式", "替换文本", "作用目标"], fields.Select(field => field.Label));
            Assert.Equal("正则表达式无效。", fields[1].Error);
            var item = FindRuleItem(view, rule);
            Assert.True(item.HasError);
            Assert.Equal("规则执行失败", item.ErrorMessage);
            Assert.Contains(
                VisualTreeTestHelper.FindDescendants<TextBlock>(item),
                textBlock => textBlock.Text == "规则执行失败" && textBlock.Visibility == Visibility.Visible);
            Assert.True(Assert.Single(VisualTreeTestHelper.FindDescendants<Button>(
                view,
                button => Equals(button.Content, "取消"))).IsEnabled);
            Assert.False(Assert.Single(VisualTreeTestHelper.FindDescendants<Button>(
                view,
                button => Equals(button.Content, "保存"))).IsEnabled);
        });
    }

    [Fact]
    public void RegexReplacementRulesPage_help_explains_scope_order_and_empty_output()
    {
        WpfTestHost.RunInSta(() =>
        {
            var context = new RegexReplacementRulesViewLayoutContext { IsHelpDrawerOpen = true };
            var view = CreateView(context, 1000, 700);
            var drawer = Assert.IsType<Border>(view.FindName("HelpDrawerBorder"));
            Assert.Equal(Visibility.Visible, ((UIElement)drawer.Parent).Visibility);
            var texts = VisualTreeTestHelper.FindDescendants<TextBlock>(drawer)
                .Select(textBlock => textBlock.Text)
                .ToHashSet(StringComparer.Ordinal);
            Assert.Contains("作用范围", texts);
            Assert.Contains("执行顺序", texts);
            Assert.Contains("空输出", texts);
            Assert.Contains("正则小抄", texts);

            context.IsHelpDrawerOpen = false;
            view.UpdateLayout();
            Assert.Equal(Visibility.Collapsed, ((UIElement)drawer.Parent).Visibility);
        });
    }

    private static RegexReplacementRulesPage CreateView(
        RegexReplacementRulesViewLayoutContext context,
        double width = 1280,
        double height = 760)
    {
        var view = new RegexReplacementRulesPage { DataContext = context };
        view.Measure(new Size(width, height));
        view.Arrange(new Rect(0, 0, width, height));
        view.UpdateLayout();
        return view;
    }

    private static RegexReplacementRuleListItemViewModel CreateRule(
        string name,
        string pattern,
        bool isEnabled,
        RegexReplacementScope scope,
        bool isSelected,
        string? error = null) =>
        new(Guid.NewGuid(), name, pattern, isEnabled, scope, isSelected, error);

    private static RuleListItemView FindRuleItem(
        RegexReplacementRulesPage view,
        RegexReplacementRuleListItemViewModel rule) =>
        Assert.IsType<RuleListItemView>(VisualTreeTestHelper.FindDescendant<RuleListItemView>(
            view,
            candidate => ReferenceEquals(candidate.CommandParameter, rule)));

    private static void AssertToolbarIcon(
        RegexReplacementRulesPage view,
        string automationName,
        SymbolRegular expectedSymbol)
    {
        var button = Assert.Single(VisualTreeTestHelper.FindDescendants<Button>(
            view,
            candidate => AutomationProperties.GetName(candidate) == automationName));
        Assert.Equal(expectedSymbol, Assert.IsType<SymbolIcon>(
            VisualTreeTestHelper.FindDescendant<SymbolIcon>(button)).Symbol);
        Assert.Equal(automationName, button.ToolTip);
    }

    private sealed partial class RegexReplacementRulesViewLayoutContext : ObservableObject
    {
        public RelayCommand NewRuleCommand { get; } = new(static () => { });
        public RelayCommand OpenHelpCommand { get; } = new(static () => { });
        public RelayCommand CloseHelpCommand { get; } = new(static () => { });
        public RelayCommand<object> SelectRuleCommand { get; } = new(static _ => { });
        public RelayCommand<object> ToggleEnabledCommand { get; } = new(static _ => { });
        public RelayCommand<object> ExportRuleCommand { get; } = new(static _ => { });
        public RelayCommand<object> CopyRuleCommand { get; } = new(static _ => { });
        public RelayCommand<object> MoveUpCommand { get; } = new(static _ => { });
        public RelayCommand<object> MoveDownCommand { get; } = new(static _ => { });
        public RelayCommand<object> DeleteRuleCommand { get; } = new(static _ => { });
        public RelayCommand<object> ReorderRuleCommand { get; } = new(static _ => { });

        public ObservableCollection<RegexReplacementRuleListItemViewModel> Rules { get; init; } = [];

        [ObservableProperty]
        private bool hasEditor;

        [ObservableProperty]
        private bool isHelpDrawerOpen;

        [ObservableProperty]
        private bool canSave = true;

        [ObservableProperty]
        private string patternValidationMessage = string.Empty;

        public bool CanCancel { get; init; } = true;
        public string DraftName { get; init; } = "规则名称";
        public string DraftPattern { get; init; } = "pattern";
        public string DraftReplacement { get; init; } = "replacement";
        public RegexReplacementScope DraftScope { get; init; } = RegexReplacementScope.Both;
        public Array Scopes { get; } = Enum.GetValues(typeof(RegexReplacementScope));
        public string NameValidationMessage { get; init; } = string.Empty;
    }
}
