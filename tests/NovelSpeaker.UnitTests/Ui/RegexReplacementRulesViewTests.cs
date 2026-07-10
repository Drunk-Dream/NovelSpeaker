using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using NovelSpeaker.App.ViewModels;
using NovelSpeaker.App.Views;
using NovelSpeaker.Domain.Books;
using Xunit;

namespace NovelSpeaker.UnitTests.Ui;

public sealed class RegexReplacementRulesViewTests
{
    [Fact]
    public void RegexReplacementRulesView_uses_theme_aware_rule_cards_and_explicit_enabled_state()
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
            var view = new RegexReplacementRulesView
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

            var statusBoxes = FindDescendants<CheckBox>(view).ToArray();
            Assert.Equal(["已启用", "已禁用"], statusBoxes.Select(box => box.Content).ToArray());
            Assert.True(statusBoxes[0].IsChecked is true);
            Assert.True(statusBoxes[1].IsChecked is false);
            Assert.Contains("规则执行失败", FindDescendants<TextBlock>(view).Select(block => block.Text));
            Assert.NotNull(FindDescendants<Border>(view).SingleOrDefault(border =>
                AutomationProperties.GetName(border) == enabledRule.AutomationName));
        });
    }

    private static IEnumerable<T> FindDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, index);
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

    private sealed class RegexReplacementRulesViewLayoutContext
    {
        public ObservableCollection<RegexReplacementRuleListItemViewModel> Rules { get; init; } = [];
        public string DraftName { get; init; } = "规则名称";
        public string DraftPattern { get; init; } = "pattern";
        public string DraftReplacement { get; init; } = "replacement";
        public RegexReplacementScope DraftScope { get; init; } = RegexReplacementScope.Both;
        public Array Scopes { get; } = Enum.GetValues(typeof(RegexReplacementScope));
        public string ValidationMessage { get; init; } = string.Empty;
    }
}
