using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using SymbolIcon = Wpf.Ui.Controls.SymbolIcon;
using SymbolRegular = Wpf.Ui.Controls.SymbolRegular;
using Xunit;

namespace NovelSpeaker.App.WpfTests.Ui;

[Collection("WpfDispatcher")]
public sealed partial class TtsRulesPageTests
{
    [Fact]
    public void TtsRulesPage_uses_split_scrollable_workspace_without_datagrid()
    {
        WpfTestHost.RunInSta(() =>
        {
            var rules = new ObservableCollection<TtsRuleListItemViewModel>();
            for (var index = 0; index < 40; index++)
            {
                rules.Add(new TtsRuleListItemViewModel(index + 1, $"规则 {index + 1}", true, index == 0, index == 0));
            }

            var context = new TtsRulesViewLayoutContext
            {
                HasEditor = true,
                Rules = rules
            };
            for (var index = 0; index < 30; index++)
            {
                context.HeaderEntries.Add(new EditableKeyValueItemViewModel($"X-{index}", $"value-{index}"));
            }

            var view = new TtsRulesPage
            {
                DataContext = context
            };

            view.Measure(new Size(1280, 760));
            view.Arrange(new Rect(0, 0, 1280, 760));
            view.UpdateLayout();

            var leftScrollViewer = Assert.IsType<ScrollViewer>(view.FindName("RulesListScrollViewer"));
            var rightScrollViewer = Assert.IsType<ScrollViewer>(view.FindName("RuleEditorScrollViewer"));

            Assert.True(leftScrollViewer.ScrollableHeight > 0);
            Assert.True(rightScrollViewer.ScrollableHeight > 0);
            Assert.Null(VisualTreeTestHelper.FindDescendant<DataGrid>(view));
        });
    }

    [Fact]
    public void TtsRulesPage_exposes_rule_item_automation_name()
    {
        WpfTestHost.RunInSta(() =>
        {
            var targetRule = new TtsRuleListItemViewModel(2, "当前规则", false, true, true);
            var view = new TtsRulesPage
            {
                DataContext = new TtsRulesViewLayoutContext
                {
                    Rules =
                    [
                        new TtsRuleListItemViewModel(1, "其他规则", true, false, false),
                        targetRule
                    ]
                }
            };

            view.Measure(new Size(960, 680));
            view.Arrange(new Rect(0, 0, 960, 680));
            view.UpdateLayout();

            var button = VisualTreeTestHelper.FindDescendant<Button>(
                view,
                candidate => AutomationProperties.GetName(candidate) == targetRule.AutomationName);

            Assert.NotNull(button);
            Assert.Equal("当前规则，已禁用，已选中", AutomationProperties.GetName(button!));
        });
    }

    [Fact]
    public void TtsRulesPage_card_contains_summary_enabled_and_more_actions_without_current_action()
    {
        WpfTestHost.RunInSta(() =>
        {
            var rule = new TtsRuleListItemViewModel(
                2,
                "备用规则",
                "POST · https://speech.example.com",
                true,
                false,
                true);
            var view = new TtsRulesPage
            {
                DataContext = new TtsRulesViewLayoutContext
                {
                    HasEditor = true,
                    Rules = [rule]
                }
            };

            view.Measure(new Size(1280, 760));
            view.Arrange(new Rect(0, 0, 1280, 760));
            view.UpdateLayout();

            Assert.NotNull(VisualTreeTestHelper.FindDescendant<TextBlock>(
                view,
                candidate => candidate.Text == rule.RequestSummary));
            Assert.NotNull(VisualTreeTestHelper.FindDescendant<Button>(
                view,
                candidate => AutomationProperties.GetName(candidate) == "切换规则启用状态：备用规则"));
            Assert.Null(VisualTreeTestHelper.FindDescendant<Button>(
                view,
                candidate => Equals(candidate.Content, "设为当前")));
            var moreButton = VisualTreeTestHelper.FindDescendant<Button>(
                view,
                candidate => AutomationProperties.GetName(candidate) == "更多操作：备用规则");
            Assert.NotNull(moreButton);
            Assert.Equal(
                ["导出", "删除"],
                moreButton!.ContextMenu!.Items
                    .Cast<MenuItem>()
                    .Select(item => (string)item.Header)
                    .ToArray());
        });
    }

    [Fact]
    public void TtsRulesPage_uses_the_shared_container_for_a_full_card_selection_surface()
    {
        WpfTestHost.RunInSta(() =>
        {
            var rule = new TtsRuleListItemViewModel(
                2,
                "整卡点击",
                "POST · https://speech.example.com",
                true,
                false,
                false);
            var view = new TtsRulesPage
            {
                DataContext = new TtsRulesViewLayoutContext
                {
                    Rules = [rule]
                }
            };

            view.Measure(new Size(1280, 760));
            view.Arrange(new Rect(0, 0, 1280, 760));
            view.UpdateLayout();

            var card = Assert.IsType<Border>(VisualTreeTestHelper.FindDescendant<Border>(
                view,
                candidate => ReferenceEquals(candidate.DataContext, rule) &&
                             AutomationProperties.GetName(candidate) == rule.AutomationName));
            var selectionButton = Assert.IsType<Button>(VisualTreeTestHelper.FindDescendant<Button>(
                card,
                candidate => AutomationProperties.GetName(candidate) == rule.AutomationName));

            Assert.Same(view.FindResource("SelectableCardListItemContainerStyle"), card.Style);
            Assert.Equal(new Thickness(1), card.BorderThickness);
            Assert.NotEqual(Brushes.Transparent, card.BorderBrush);
            Assert.InRange(Math.Abs(selectionButton.ActualWidth - card.ActualWidth), 0d, 2d);
            Assert.InRange(Math.Abs(selectionButton.ActualHeight - card.ActualHeight), 0d, 2d);
        });
    }

    [Fact]
    public void TtsRulesPage_right_editor_keeps_only_audition_cancel_and_save_actions()
    {
        WpfTestHost.RunInSta(() =>
        {
            var view = new TtsRulesPage
            {
                DataContext = new TtsRulesViewLayoutContext
                {
                    HasEditor = true,
                    CanSaveDraft = false,
                    CanCancelEditing = false,
                    Rules =
                    [
                        new TtsRuleListItemViewModel(
                            1,
                            "规则一",
                            "GET · https://example.com",
                            true,
                            true,
                            true)
                    ]
                }
            };

            view.Measure(new Size(1280, 760));
            view.Arrange(new Rect(0, 0, 1280, 760));
            view.UpdateLayout();

            var audition = Assert.Single(VisualTreeTestHelper.FindDescendants<Button>(
                view,
                candidate => Equals(candidate.Content, "试听")));
            var cancel = Assert.Single(VisualTreeTestHelper.FindDescendants<Button>(
                view,
                candidate => Equals(candidate.Content, "取消")));
            var save = Assert.Single(VisualTreeTestHelper.FindDescendants<Button>(
                view,
                candidate => Equals(candidate.Content, "保存")));

            Assert.True(audition.IsEnabled);
            Assert.False(cancel.IsEnabled);
            Assert.False(save.IsEnabled);
            Assert.Null(VisualTreeTestHelper.FindDescendant<Button>(
                view,
                candidate => Equals(candidate.Content, "导出")));
            Assert.Null(VisualTreeTestHelper.FindDescendant<Button>(
                view,
                candidate => Equals(candidate.Content, "删除")));
        });
    }

    [Fact]
    public void TtsRulesPage_uses_icon_buttons_for_toolbar_import_actions()
    {
        WpfTestHost.RunInSta(() =>
        {
            var view = new TtsRulesPage
            {
                DataContext = new TtsRulesViewLayoutContext()
            };

            view.Measure(new Size(1280, 760));
            view.Arrange(new Rect(0, 0, 1280, 760));
            view.UpdateLayout();

            AssertToolbarIcon(view, "新建规则", SymbolRegular.DocumentAdd24);
            AssertToolbarIcon(view, "导入文件", SymbolRegular.ArrowImport24);
            AssertToolbarIcon(view, "从剪贴板", SymbolRegular.ClipboardPaste24);
        });
    }

    private static void AssertToolbarIcon(TtsRulesPage view, string automationName, SymbolRegular expectedSymbol)
    {
        var button = Assert.Single(VisualTreeTestHelper.FindDescendants<Button>(
            view,
            candidate => AutomationProperties.GetName(candidate) == automationName));
        Assert.Equal(
            expectedSymbol,
            Assert.IsType<SymbolIcon>(VisualTreeTestHelper.FindDescendant<SymbolIcon>(button)).Symbol);
        Assert.Equal(automationName, button.ToolTip);
    }

    [Fact]
    public void TtsRulesPage_toggles_help_drawer_visibility()
    {
        WpfTestHost.RunInSta(() =>
        {
            var context = new TtsRulesViewLayoutContext
            {
                IsHelpDrawerOpen = true
            };
            var view = new TtsRulesPage
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
    public void TtsRulesPage_help_drawer_contains_guided_get_and_post_examples()
    {
        WpfTestHost.RunInSta(() =>
        {
            var view = new TtsRulesPage
            {
                DataContext = new TtsRulesViewLayoutContext
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

            Assert.Contains("从零开始", helpTexts);
            Assert.Contains("GET 示例", helpTexts);
            Assert.Contains("POST JSON 示例", helpTexts);
            Assert.Contains("https://example.com/tts?text={{encodeURIComponent(speakText)}}&speed={{speakSpeed}}", helpTexts);
            Assert.Contains("失败时按这个顺序检查", helpTexts);
        });
    }

    [Fact]
    public void TtsRulesPage_keeps_both_panes_visible_at_minimum_supported_width()
    {
        WpfTestHost.RunInSta(() =>
        {
            var view = new TtsRulesPage
            {
                DataContext = new TtsRulesViewLayoutContext
                {
                    HasEditor = true,
                    Rules = [new TtsRuleListItemViewModel(1, "规则一", true, true, true)]
                }
            };

            view.Measure(new Size(900, 640));
            view.Arrange(new Rect(0, 0, 900, 640));
            view.UpdateLayout();

            var leftScrollViewer = Assert.IsType<ScrollViewer>(view.FindName("RulesListScrollViewer"));
            var rightScrollViewer = Assert.IsType<ScrollViewer>(view.FindName("RuleEditorScrollViewer"));

            Assert.True(leftScrollViewer.ActualWidth > 0);
            Assert.True(rightScrollViewer.ActualWidth > 0);
        });
    }

    [Fact]
    public void TtsRulesPage_hides_removed_rule_controls_and_preview_area()
    {
        WpfTestHost.RunInSta(() =>
        {
            var view = new TtsRulesPage
            {
                DataContext = new TtsRulesViewLayoutContext
                {
                    HasEditor = true,
                    Rules = [new TtsRuleListItemViewModel(1, "规则一", true, true, true)]
                }
            };

            view.Measure(new Size(1280, 760));
            view.Arrange(new Rect(0, 0, 1280, 760));
            view.UpdateLayout();

            Assert.Null(VisualTreeTestHelper.FindDescendant<TextBlock>(view, candidate => candidate.Text == "LoginInfo"));
            Assert.Null(VisualTreeTestHelper.FindDescendant<Expander>(view, candidate => Equals(candidate.Header, "高级设置")));
            Assert.Null(VisualTreeTestHelper.FindDescendant<TextBlock>(view, candidate => candidate.Text == "请求预览与结果"));
            Assert.Null(VisualTreeTestHelper.FindDescendant<Button>(view, candidate => Equals(candidate.Content, "生成预览")));
            Assert.Null(VisualTreeTestHelper.FindDescendant<Button>(view, candidate => Equals(candidate.Content, "取消试听")));
            Assert.Null(VisualTreeTestHelper.FindDescendant<Button>(view, candidate => Equals(candidate.Content, "清除 Cookie")));
            Assert.Null(VisualTreeTestHelper.FindDescendant<TextBlock>(view, candidate => candidate.Text == "超时 (ms)"));
        });
    }

    [Fact]
    public void TtsRulesPage_shows_request_body_only_for_post()
    {
        WpfTestHost.RunInSta(() =>
        {
            var getView = new TtsRulesPage
            {
                DataContext = new TtsRulesViewLayoutContext
                {
                    HasEditor = true,
                    DraftRequestMethod = "GET",
                    Rules = [new TtsRuleListItemViewModel(1, "规则一", true, true, true)]
                }
            };
            getView.Measure(new Size(1280, 760));
            getView.Arrange(new Rect(0, 0, 1280, 760));
            getView.UpdateLayout();

            var hiddenRequestBodyLabel = VisualTreeTestHelper.FindDescendant<TextBlock>(getView, candidate => candidate.Text == "请求体");
            Assert.NotNull(hiddenRequestBodyLabel);
            Assert.Equal(Visibility.Collapsed, ((FrameworkElement)hiddenRequestBodyLabel!.Parent).Visibility);

            var postView = new TtsRulesPage
            {
                DataContext = new TtsRulesViewLayoutContext
                {
                    HasEditor = true,
                    DraftRequestMethod = "POST",
                    Rules = [new TtsRuleListItemViewModel(1, "规则一", true, true, true)]
                }
            };
            postView.Measure(new Size(1280, 760));
            postView.Arrange(new Rect(0, 0, 1280, 760));
            postView.UpdateLayout();

            var visibleRequestBodyLabel = VisualTreeTestHelper.FindDescendant<TextBlock>(postView, candidate => candidate.Text == "请求体");
            Assert.NotNull(visibleRequestBodyLabel);
            Assert.Equal(Visibility.Visible, ((FrameworkElement)visibleRequestBodyLabel!.Parent).Visibility);
        });
    }

    [Fact]
    public void TtsRulesPage_shows_concurrent_rate_format_tooltip()
    {
        WpfTestHost.RunInSta(() =>
        {
            var view = new TtsRulesPage
            {
                DataContext = new TtsRulesViewLayoutContext
                {
                    HasEditor = true,
                    Rules = [new TtsRuleListItemViewModel(1, "规则一", true, true, true)]
                }
            };

            view.Measure(new Size(1280, 760));
            view.Arrange(new Rect(0, 0, 1280, 760));
            view.UpdateLayout();

            var tooltipTextBox = VisualTreeTestHelper.FindDescendant<TextBox>(
                view,
                candidate => Equals(candidate.ToolTip, "格式：次数/毫秒，例如 2/1000"));

            Assert.NotNull(tooltipTextBox);
        });
    }

    private sealed partial class TtsRulesViewLayoutContext : ObservableObject
    {
        public ObservableCollection<TtsRuleListItemViewModel> Rules { get; init; } = [];

        public ObservableCollection<EditableKeyValueItemViewModel> HeaderEntries { get; } = [];

        [ObservableProperty]
        private bool hasEditor;

        public bool IsEditingNewRule { get; init; }

        public bool HasUnsavedChanges { get; init; }

        public bool IsBusy { get; init; }

        public bool IsTestBusy { get; init; }

        [ObservableProperty]
        private bool isHelpDrawerOpen;

        public string DraftName { get; init; } = "当前规则";

        public bool DraftIsEnabled { get; init; } = true;

        public string DraftUrl { get; init; } = "https://example.com/tts";

        public string DraftRequestMethod { get; init; } = "GET";

        public string DraftContentType { get; init; } = "audio/mpeg";

        public string DraftRequestBody { get; init; } = string.Empty;

        public string DraftConcurrentRate { get; init; } = "2/1000";

        public bool CanSaveDraft { get; init; }

        public bool CanCancelEditing { get; init; }

        public bool CanDeleteCurrentRule => true;

        public bool CanSetCurrentRule => true;

        public bool CanExportDraft => true;

        public bool CanTestDraft => true;

        public bool IsPostMethod => string.Equals(DraftRequestMethod, "POST", StringComparison.OrdinalIgnoreCase);
    }
}
