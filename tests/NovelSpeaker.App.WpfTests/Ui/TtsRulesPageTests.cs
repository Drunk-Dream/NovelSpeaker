using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovelSpeaker.App.Shared.Presentation.Controls.Forms;
using NovelSpeaker.App.Shared.Presentation.Rules;
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

            var rulesList = Assert.IsType<ListBox>(view.FindName("RulesList"));
            var leftScrollViewer = Assert.IsAssignableFrom<ScrollViewer>(
                VisualTreeTestHelper.FindDescendant<ScrollViewer>(rulesList));
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

            var item = VisualTreeTestHelper.FindDescendant<RuleListItemView>(
                view,
                candidate => AutomationProperties.GetName(candidate) == targetRule.AutomationName);

            Assert.NotNull(item);
            Assert.Equal("当前规则，已禁用，已选中", AutomationProperties.GetName(item!));
        });
    }

    [Fact]
    public void TtsRulesPage_uses_shared_rule_item_with_context_actions_without_current_action()
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

            var item = Assert.IsType<RuleListItemView>(VisualTreeTestHelper.FindDescendant<RuleListItemView>(
                view,
                candidate => ReferenceEquals(candidate.CommandParameter, rule)));

            Assert.Equal(rule.Name, item.Title);
            Assert.Equal(rule.RequestSummary, item.Summary);
            Assert.True(item.IsRuleEnabled);
            Assert.True(item.IsSelected);
            Assert.False(item.IsSortable);
            item.ContextMenu!.PlacementTarget = item;
            item.ContextMenu.IsOpen = true;
            var visibleHeaders = item.ContextMenu.Items
                .OfType<MenuItem>()
                .Where(menuItem => menuItem.Visibility == Visibility.Visible)
                .Select(menuItem => (string)menuItem.Header)
                .ToArray();
            Assert.Equal(
                ["导出到文件", "复制到剪切板", "删除"],
                visibleHeaders);
            Assert.DoesNotContain(
                VisualTreeTestHelper.FindDescendants<FrameworkElement>(view),
                candidate => AutomationProperties.GetName(candidate).Contains("设为当前", StringComparison.Ordinal));
            Assert.DoesNotContain(
                VisualTreeTestHelper.FindDescendants<Button>(view),
                candidate => AutomationProperties.GetName(candidate).Contains("更多操作", StringComparison.Ordinal));
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
            AssertToolbarIcon(view, "从文件导入", SymbolRegular.ArrowImport24);
            AssertToolbarIcon(view, "从剪切板导入", SymbolRegular.ClipboardPaste24);
            AssertToolbarIcon(view, "规则编写帮助", SymbolRegular.QuestionCircle24);
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
            Assert.Equal(0.45d, dismissOverlay.Opacity);

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

            var hiddenRequestBodyField = Assert.Single(VisualTreeTestHelper.FindDescendants<AppFormField>(
                getView,
                candidate => candidate.Label == "请求体"));
            Assert.Equal(Visibility.Collapsed, hiddenRequestBodyField.Visibility);

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

            var visibleRequestBodyField = Assert.Single(VisualTreeTestHelper.FindDescendants<AppFormField>(
                postView,
                candidate => candidate.Label == "请求体"));
            Assert.Equal(Visibility.Visible, visibleRequestBodyField.Visibility);
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
        public RelayCommand NewRuleCommand { get; } = new(static () => { });

        public RelayCommand OpenHelpCommand { get; } = new(static () => { });

        public RelayCommand<TtsRuleListItemViewModel> SelectRuleCommand { get; } = new(static _ => { });

        public RelayCommand<TtsRuleListItemViewModel> ToggleRuleEnabledCommand { get; } = new(static _ => { });

        public RelayCommand<TtsRuleListItemViewModel> ExportRuleCommand { get; } = new(static _ => { });

        public RelayCommand<TtsRuleListItemViewModel> CopyRuleCommand { get; } = new(static _ => { });

        public RelayCommand<TtsRuleListItemViewModel> DeleteRuleCommand { get; } = new(static _ => { });

        public ObservableCollection<TtsRuleListItemViewModel> Rules { get; init; } = [];

        public ObservableCollection<EditableKeyValueItemViewModel> HeaderEntries { get; } = [];

        [ObservableProperty]
        private bool hasEditor;

        [ObservableProperty]
        private bool isHelpDrawerOpen;

        public string DraftName { get; init; } = "当前规则";

        public string DraftUrl { get; init; } = "https://example.com/tts";

        public string DraftRequestMethod { get; init; } = "GET";

        public string DraftContentType { get; init; } = "audio/mpeg";

        public string DraftRequestBody { get; init; } = string.Empty;

        public string DraftConcurrentRate { get; init; } = "2/1000";

        public bool CanSaveDraft { get; init; }

        public bool CanCancelEditing { get; init; }

        public bool CanTestDraft => true;

        public bool IsPostMethod => string.Equals(DraftRequestMethod, "POST", StringComparison.OrdinalIgnoreCase);
    }
}
