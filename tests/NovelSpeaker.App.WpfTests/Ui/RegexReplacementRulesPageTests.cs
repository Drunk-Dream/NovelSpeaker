using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using NovelSpeaker.Domain.Books;
using SymbolIcon = Wpf.Ui.Controls.SymbolIcon;
using SymbolRegular = Wpf.Ui.Controls.SymbolRegular;
using Xunit;

namespace NovelSpeaker.App.WpfTests.Ui;

[Collection("WpfDispatcher")]
public sealed class RegexReplacementRulesPageTests
{
    [Fact]
    public void RegexReplacementRulesPage_uses_accessible_icon_for_new_rule_tool()
    {
        WpfTestHost.RunInSta(() =>
        {
            var view = new RegexReplacementRulesPage
            {
                DataContext = new RegexReplacementRulesViewLayoutContext()
            };

            view.Measure(new Size(960, 680));
            view.Arrange(new Rect(0, 0, 960, 680));
            view.UpdateLayout();

            var button = Assert.IsType<Button>(VisualTreeTestHelper.FindDescendant<Button>(
                view,
                candidate => AutomationProperties.GetName(candidate) == "新建规则"));

            Assert.Equal("新建规则", button.ToolTip);
            Assert.Equal(
                SymbolRegular.DocumentAdd24,
                Assert.IsType<SymbolIcon>(button.Content).Symbol);
        });
    }

    [Fact]
    public void RegexReplacementRulesPage_uses_theme_aware_rule_cards_and_explicit_enabled_state()
    {
        WpfTestHost.RunInSta(() =>
        {
            var enabledRule = new RegexReplacementRuleListItemViewModel(
                Guid.NewGuid(),
                "朗读替换",
                "\\s+",
                true,
                RegexReplacementScope.Speech,
                true,
                null);
            var disabledRule = new RegexReplacementRuleListItemViewModel(
                Guid.NewGuid(),
                "显示替换",
                "旧文本",
                false,
                RegexReplacementScope.Display,
                false,
                "规则执行失败");
            var view = new RegexReplacementRulesPage
            {
                DataContext = new RegexReplacementRulesViewLayoutContext
                {
                    Rules = [enabledRule, disabledRule]
                }
            };

            view.Measure(new Size(960, 680));
            view.Arrange(new Rect(0, 0, 960, 680));
            view.UpdateLayout();

            Assert.Null(VisualTreeTestHelper.FindDescendant<DataGrid>(view));
            var rulesList = Assert.IsType<ListBox>(VisualTreeTestHelper.FindDescendant<ListBox>(view));
            Assert.True(VirtualizingPanel.GetIsVirtualizing(rulesList));
            Assert.Equal(VirtualizationMode.Recycling, VirtualizingPanel.GetVirtualizationMode(rulesList));

            var statusBoxes = VisualTreeTestHelper.FindDescendants<CheckBox>(view).ToArray();
            Assert.Equal(["启用", "启用"], statusBoxes.Select(box => box.Content).ToArray());
            Assert.True(statusBoxes[0].IsChecked is true);
            Assert.True(statusBoxes[1].IsChecked is false);
            Assert.Contains("规则执行失败", VisualTreeTestHelper.FindDescendants<TextBlock>(view).Select(block => block.Text));
            Assert.NotNull(VisualTreeTestHelper.FindDescendants<Border>(view).SingleOrDefault(border =>
                AutomationProperties.GetName(border) == enabledRule.AutomationName));
        });
    }

    [Fact]
    public void RegexReplacementRulesPage_places_fallback_actions_in_more_menu_and_exposes_help()
    {
        WpfTestHost.RunInSta(() =>
        {
            var rule = new RegexReplacementRuleListItemViewModel(
                Guid.NewGuid(),
                "空白清理",
                "\\s+",
                true,
                RegexReplacementScope.Both,
                true,
                null);
            var view = new RegexReplacementRulesPage
            {
                DataContext = new RegexReplacementRulesViewLayoutContext
                {
                    Rules = [rule],
                    HasEditor = true
                }
            };

            view.Measure(new Size(960, 680));
            view.Arrange(new Rect(0, 0, 960, 680));
            view.UpdateLayout();

            var moreButton = Assert.IsType<Button>(VisualTreeTestHelper.FindDescendant<Button>(
                view,
                candidate => AutomationProperties.GetName(candidate) == "更多操作：空白清理"));
            var menu = Assert.IsType<ContextMenu>(moreButton.ContextMenu);
            Assert.Equal(
                ["上移", "下移", "删除"],
                menu.Items.OfType<MenuItem>().Select(item => item.Header).ToArray());
            Assert.NotNull(VisualTreeTestHelper.FindDescendant<Button>(
                view,
                candidate => AutomationProperties.GetName(candidate) == "正则替换帮助"));
            Assert.DoesNotContain(
                VisualTreeTestHelper.FindDescendants<Button>(view),
                button => Equals(button.Content, "删除"));
        });
    }

    private sealed class RegexReplacementRulesViewLayoutContext
    {
        public ObservableCollection<RegexReplacementRuleListItemViewModel> Rules { get; init; } = [];
        public bool HasEditor { get; init; }
        public bool CanCancel { get; init; }
        public bool CanSave { get; init; }
        public bool IsHelpDrawerOpen { get; init; }
        public string DraftName { get; init; } = "规则名称";
        public string DraftPattern { get; init; } = "pattern";
        public string DraftReplacement { get; init; } = "replacement";
        public RegexReplacementScope DraftScope { get; init; } = RegexReplacementScope.Both;
        public Array Scopes { get; } = Enum.GetValues(typeof(RegexReplacementScope));
        public string ValidationMessage { get; init; } = string.Empty;
    }
}
