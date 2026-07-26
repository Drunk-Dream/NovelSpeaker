using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using CommunityToolkit.Mvvm.ComponentModel;
using SymbolIcon = Wpf.Ui.Controls.SymbolIcon;
using SymbolRegular = Wpf.Ui.Controls.SymbolRegular;
using Xunit;

namespace NovelSpeaker.App.WpfTests.Ui;

[Collection("WpfDispatcher")]
public sealed partial class ChapterRulesPageTests
{
    [Fact]
    public void ChapterRulesPage_uses_accessible_icon_for_new_rule_tool()
    {
        WpfTestHost.RunInSta(() =>
        {
            var view = new ChapterRulesPage
            {
                DataContext = new ChapterRulesViewLayoutContext()
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
    public void ChapterRulesPage_uses_split_scrollable_workspace_without_datagrid()
    {
        WpfTestHost.RunInSta(() =>
        {
            var context = new ChapterRulesViewLayoutContext
            {
                HasEditor = true,
                Rules =
                [
                    new ChapterRuleListItemViewModel("builtin:one", "内置规则", @"^\s*第一章$", true, true, true),
                    new ChapterRuleListItemViewModel("custom:two", "自定义规则", @"^\s*第二章$", false, false, false)
                ]
            };
            for (var index = 0; index < 30; index++)
            {
                context.Rules.Add(new ChapterRuleListItemViewModel($"custom:{index + 3}", $"规则 {index + 3}", $@"^\s*第{index + 3}章$", true, false, false));
            }

            var view = new ChapterRulesPage
            {
                DataContext = context
            };

            view.Measure(new Size(1280, 760));
            view.Arrange(new Rect(0, 0, 1280, 760));
            view.UpdateLayout();

            var leftScrollViewer = Assert.IsType<ScrollViewer>(view.FindName("RulesListScrollViewer"));
            var rightScrollViewer = Assert.IsType<ScrollViewer>(view.FindName("RuleEditorScrollViewer"));

            Assert.True(leftScrollViewer.ScrollableHeight > 0);
            Assert.True(rightScrollViewer.ActualWidth > 0);
            Assert.Null(VisualTreeTestHelper.FindDescendant<DataGrid>(view));
        });
    }

    [Fact]
    public void ChapterRulesPage_exposes_rule_item_automation_name()
    {
        WpfTestHost.RunInSta(() =>
        {
            var item = new ChapterRuleListItemViewModel("builtin:one", "内置规则", @"^\s*第一章$", false, true, true);
            var view = new ChapterRulesPage
            {
                DataContext = new ChapterRulesViewLayoutContext
                {
                    Rules = [item]
                }
            };

            view.Measure(new Size(960, 680));
            view.Arrange(new Rect(0, 0, 960, 680));
            view.UpdateLayout();

            var border = VisualTreeTestHelper.FindDescendant<Border>(
                view,
                candidate => AutomationProperties.GetName(candidate) == item.AutomationName);
            var button = Assert.IsType<Button>(VisualTreeTestHelper.FindDescendant<Button>(border!, candidate =>
                AutomationProperties.GetName(candidate) == item.AutomationName));

            Assert.NotNull(border);
            Assert.Equal("内置规则，已禁用，已选中", AutomationProperties.GetName(border!));
            Assert.InRange(Math.Abs(button.ActualWidth - border!.ActualWidth), 0d, 1d);
            Assert.True(
                Math.Abs(button.ActualHeight - border.ActualHeight) <= 2d,
                $"buttonHeight={button.ActualHeight}, cardHeight={border.ActualHeight}");
        });
    }

    [Fact]
    public void ChapterRulesPage_keeps_rule_summary_above_quick_actions_at_narrow_width()
    {
        WpfTestHost.RunInSta(() =>
        {
            var item = new ChapterRuleListItemViewModel(
                "custom:narrow",
                "章节数字",
                @"^\s*第[0-9一二三四五六七八九十百千万]+章\s*$",
                true,
                false,
                false);
            var view = new ChapterRulesPage
            {
                DataContext = new ChapterRulesViewLayoutContext
                {
                    Rules = [item]
                }
            };

            view.Measure(new Size(368, 640));
            view.Arrange(new Rect(0, 0, 368, 640));
            view.UpdateLayout();

            var border = Assert.IsType<Border>(VisualTreeTestHelper.FindDescendant<Border>(view, candidate =>
                AutomationProperties.GetName(candidate) == item.AutomationName));
            var pattern = Assert.IsType<TextBlock>(VisualTreeTestHelper.FindDescendant<TextBlock>(border, candidate =>
                candidate.Text == item.PatternSummary));
            var checkBox = Assert.IsType<CheckBox>(VisualTreeTestHelper.FindDescendant<CheckBox>(border));
            var patternBounds = GetBoundsRelativeTo(pattern, border);
            var checkBoxBounds = GetBoundsRelativeTo(checkBox, border);

            Assert.True(
                patternBounds.Bottom <= checkBoxBounds.Top,
                $"patternBottom={patternBounds.Bottom}, quickActionsTop={checkBoxBounds.Top}");
        });
    }

    [Fact]
    public void ChapterRulesPage_shows_enable_checkbox_only_in_left_rule_list()
    {
        WpfTestHost.RunInSta(() =>
        {
            var view = new ChapterRulesPage
            {
                DataContext = new ChapterRulesViewLayoutContext
                {
                    HasEditor = true,
                    Rules =
                    [
                        new ChapterRuleListItemViewModel("custom:one", "规则一", @"^\s*一$", true, false, true)
                    ]
                }
            };

            view.Measure(new Size(960, 680));
            view.Arrange(new Rect(0, 0, 960, 680));
            view.UpdateLayout();

            var checkBoxes = VisualTreeTestHelper.FindDescendants<CheckBox>(view).ToArray();

            Assert.Single(checkBoxes);
            Assert.Equal("启用", checkBoxes[0].Content);
            Assert.Equal("切换规则启用状态：规则一", AutomationProperties.GetName(checkBoxes[0]));
        });
    }

    [Fact]
    public void ChapterRulesPage_card_contains_summary_enable_drag_handle_and_more_menu()
    {
        WpfTestHost.RunInSta(() =>
        {
            var customRule = new ChapterRuleListItemViewModel(
                "custom:one",
                "规则一",
                @"^\s*第一章$",
                true,
                false,
                true,
                true)
            {
                CanMoveUp = false,
                CanMoveDown = true
            };
            var builtInRule = new ChapterRuleListItemViewModel(
                "builtin:two",
                "规则二",
                @"^\s*第二章$",
                true,
                true,
                false,
                false);
            var view = new ChapterRulesPage
            {
                DataContext = new ChapterRulesViewLayoutContext
                {
                    Rules = [customRule, builtInRule]
                }
            };

            view.Measure(new Size(960, 680));
            view.Arrange(new Rect(0, 0, 960, 680));
            view.UpdateLayout();

            Assert.NotNull(VisualTreeTestHelper.FindDescendant<TextBlock>(
                view,
                candidate => candidate.Text == customRule.PatternSummary));
            Assert.NotNull(VisualTreeTestHelper.FindDescendant<Button>(
                view,
                candidate => AutomationProperties.GetName(candidate) == "拖动排序：规则一"));

            var customMore = Assert.IsType<Button>(VisualTreeTestHelper.FindDescendant<Button>(
                view,
                candidate => AutomationProperties.GetName(candidate) == "更多操作：规则一"));
            var customItems = customMore.ContextMenu!.Items.OfType<MenuItem>().ToArray();
            Assert.Equal(["上移", "下移", "删除"], customItems.Select(item => item.Header));
            Assert.Equal(
                ["CanMoveUp", "CanMoveDown", "CanDeleteAction"],
                customItems
                    .Select(item => System.Windows.Data.BindingOperations
                        .GetBinding(item, MenuItem.IsEnabledProperty)!.Path.Path!)
                    .ToArray());

            var builtInMore = Assert.IsType<Button>(VisualTreeTestHelper.FindDescendant<Button>(
                view,
                candidate => AutomationProperties.GetName(candidate) == "更多操作：规则二"));
            var delete = builtInMore.ContextMenu!.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "删除"));
            Assert.Equal("CanDeleteAction", System.Windows.Data.BindingOperations
                .GetBinding(delete, MenuItem.IsEnabledProperty)?.Path.Path);
            Assert.False(builtInRule.CanDeleteAction);

            Assert.DoesNotContain(
                VisualTreeTestHelper.FindDescendants<TextBlock>(view),
                textBlock => textBlock.Text is "内置" or "自定义");
        });
    }

    [Fact]
    public void ChapterRulesPage_right_editor_keeps_only_help_cancel_and_save_actions()
    {
        WpfTestHost.RunInSta(() =>
        {
            var view = new ChapterRulesPage
            {
                DataContext = new ChapterRulesViewLayoutContext
                {
                    HasEditor = true,
                    CanSaveDraft = false,
                    CanCancelEditing = false,
                    Rules =
                    [
                        new ChapterRuleListItemViewModel(
                            "custom:one",
                            "规则一",
                            @"^\s*第一章$",
                            true,
                            false,
                            true)
                    ]
                }
            };

            view.Measure(new Size(960, 680));
            view.Arrange(new Rect(0, 0, 960, 680));
            view.UpdateLayout();

            var help = Assert.Single(VisualTreeTestHelper.FindDescendants<Button>(
                view,
                candidate => AutomationProperties.GetName(candidate) == "章节规则帮助"));
            var cancel = Assert.Single(VisualTreeTestHelper.FindDescendants<Button>(
                view,
                candidate => Equals(candidate.Content, "取消")));
            var save = Assert.Single(VisualTreeTestHelper.FindDescendants<Button>(
                view,
                candidate => Equals(candidate.Content, "保存")));

            Assert.Equal("章节规则帮助", help.ToolTip);
            Assert.False(cancel.IsEnabled);
            Assert.False(save.IsEnabled);
            Assert.Null(VisualTreeTestHelper.FindDescendant<Button>(
                view,
                candidate => Equals(candidate.Content, "删除")));
        });
    }

    [Fact]
    public void ChapterRulesPage_toggles_help_drawer_visibility()
    {
        WpfTestHost.RunInSta(() =>
        {
            var context = new ChapterRulesViewLayoutContext
            {
                IsHelpDrawerOpen = true
            };
            var view = new ChapterRulesPage
            {
                DataContext = context
            };

            view.Measure(new Size(1000, 700));
            view.Arrange(new Rect(0, 0, 1000, 700));
            view.UpdateLayout();

            var helpDrawer = Assert.IsType<Border>(view.FindName("HelpDrawerBorder"));
            var dismissOverlay = Assert.IsType<Button>(view.FindName("HelpDrawerDismissOverlay"));
            Assert.Equal(Visibility.Visible, helpDrawer.Visibility);
            Assert.Equal(Visibility.Visible, ((UIElement)helpDrawer.Parent).Visibility);
            Assert.NotEqual(Brushes.Transparent, helpDrawer.Background);
            Assert.Equal(1d, dismissOverlay.Opacity);

            context.IsHelpDrawerOpen = false;
            view.UpdateLayout();

            Assert.Equal(Visibility.Visible, helpDrawer.Visibility);
            Assert.Equal(Visibility.Collapsed, ((UIElement)helpDrawer.Parent).Visibility);
        });
    }

    [Fact]
    public void ChapterRulesPage_help_drawer_explains_matching_order_and_regex_starting_point()
    {
        WpfTestHost.RunInSta(() =>
        {
            var view = new ChapterRulesPage
            {
                DataContext = new ChapterRulesViewLayoutContext
                {
                    IsHelpDrawerOpen = true
                }
            };

            view.Measure(new Size(1000, 700));
            view.Arrange(new Rect(0, 0, 1000, 700));
            view.UpdateLayout();

            var helpTexts = VisualTreeTestHelper.FindDescendants<TextBlock>(view)
                .Select(textBlock => textBlock.Text)
                .ToHashSet(StringComparer.Ordinal);

            Assert.Contains("规则如何生效", helpTexts);
            Assert.Contains("从一个规则开始", helpTexts);
            Assert.Contains(@"^\s*第[0-9一二三四五六七八九十百千零两]+章(?:\s+.+)?\s*$", helpTexts);
            Assert.Contains("排序与误识别", helpTexts);
        });
    }

    [Fact]
    public void ChapterRulesPage_marks_drag_target_visually()
    {
        WpfTestHost.RunInSta(() =>
        {
            var first = new ChapterRuleListItemViewModel("custom:one", "规则一", @"^\s*一$", true, false, false);
            var second = new ChapterRuleListItemViewModel("custom:two", "规则二", @"^\s*二$", true, false, false)
            {
                IsDropTarget = true
            };
            var view = new ChapterRulesPage
            {
                DataContext = new ChapterRulesViewLayoutContext
                {
                    Rules = [first, second]
                }
            };

            view.Measure(new Size(960, 680));
            view.Arrange(new Rect(0, 0, 960, 680));
            view.UpdateLayout();

            var firstBorder = Assert.Single(VisualTreeTestHelper.FindDescendants<Border>(
                view,
                candidate => ReferenceEquals(candidate.DataContext, first) && candidate.Child is Grid));
            var secondBorder = Assert.Single(VisualTreeTestHelper.FindDescendants<Border>(
                view,
                candidate => ReferenceEquals(candidate.DataContext, second) && candidate.Child is Grid));

            Assert.NotEqual(firstBorder.Background, secondBorder.Background);
        });
    }

    [Fact]
    public void ChapterRulesPage_constrains_workspace_height_to_page()
    {
        WpfTestHost.RunInSta(() =>
        {
            var provider = WpfTestHost.BuildServiceProvider();
            try
            {
                var page = provider.GetRequiredService<ChapterRulesPage>();

                page.Measure(new Size(1280, 760));
                page.Arrange(new Rect(0, 0, 1280, 760));
                page.UpdateLayout();

                Assert.InRange(Math.Abs(page.ActualHeight - 760d), 0d, 1d);
            }
            finally
            {
                provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
    }

    private static Rect GetBoundsRelativeTo(FrameworkElement element, FrameworkElement root)
    {
        var topLeft = element.TranslatePoint(new Point(0, 0), root);
        return new Rect(topLeft.X, topLeft.Y, element.ActualWidth, element.ActualHeight);
    }

    private sealed partial class ChapterRulesViewLayoutContext : ObservableObject
    {
        public ObservableCollection<ChapterRuleListItemViewModel> Rules { get; init; } = [];

        [ObservableProperty]
        private bool hasEditor;

        [ObservableProperty]
        private bool isHelpDrawerOpen;

        public string DraftName { get; init; } = "当前规则";

        public string DraftPattern { get; init; } = @"^\s*第一章$";

        public bool CanSaveDraft { get; init; } = true;

        public bool CanCancelEditing { get; init; } = true;

        public string NameValidationMessage { get; init; } = string.Empty;

        public string PatternValidationMessage { get; init; } = string.Empty;

    }
}
