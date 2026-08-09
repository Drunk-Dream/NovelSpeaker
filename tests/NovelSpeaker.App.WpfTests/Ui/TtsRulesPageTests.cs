using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovelSpeaker.App.Shared.Presentation.Controls.Common;
using NovelSpeaker.App.Shared.Presentation.Controls.Forms;
using NovelSpeaker.App.Shared.Presentation.Rules;
using NovelSpeaker.App.Shared.Theming;
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
    public void TtsRulesPage_uses_formal_workbench_controls()
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

            var header = Assert.IsType<AppPageHeader>(view.FindName("PageHeader"));
            Assert.Equal("TTS 规则", header.Title);
            Assert.Empty(header.Description);
            Assert.NotNull(header.Actions);
            Assert.IsType<AppSectionSurface>(view.FindName("RulesSurface"));
            Assert.IsType<AppSectionSurface>(view.FindName("EditorSurface"));
            Assert.NotEmpty(VisualTreeTestHelper.FindDescendants<AppFormField>(view));
            Assert.NotNull(VisualTreeTestHelper.FindDescendant<RuleListItemView>(
                view,
                candidate => ReferenceEquals(candidate.CommandParameter, rule)));
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
    public void TtsRulesPage_toolbar_icons_follow_dark_theme_foreground()
    {
        WpfTestHost.RunInSta(() =>
        {
            var runtime = new WpfUiThemeRuntime();
            runtime.ApplyDarkTheme();
            try
            {
                var view = new TtsRulesPage
                {
                    DataContext = new TtsRulesViewLayoutContext()
                };
                view.Measure(new Size(1280, 760));
                view.Arrange(new Rect(0, 0, 1280, 760));
                view.UpdateLayout();
                var expected = Assert.IsAssignableFrom<Brush>(view.FindResource("App.Brush.Text.Primary"));

                foreach (var automationName in new[]
                         {
                             "新建规则", "从文件导入", "从剪切板导入", "规则编写帮助"
                         })
                {
                    var button = Assert.Single(VisualTreeTestHelper.FindDescendants<Button>(
                        view,
                        candidate => AutomationProperties.GetName(candidate) == automationName));
                    var icon = Assert.IsType<SymbolIcon>(VisualTreeTestHelper.FindDescendant<SymbolIcon>(button));
                    Assert.True(button.IsEnabled);
                    Assert.Equal(expected, button.Foreground);
                    Assert.Equal(expected, icon.Foreground);
                }
            }
            finally
            {
                runtime.ApplyLightTheme();
            }
        });
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

            var rulesList = Assert.IsType<ListBox>(view.FindName("RulesList"));
            var leftScrollViewer = Assert.IsAssignableFrom<ScrollViewer>(
                VisualTreeTestHelper.FindDescendant<ScrollViewer>(rulesList));
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

    [Fact]
    public void Tts_rules_visual_review_generates_stable_page_screenshots()
    {
        if (!VisualArtifactTestGuard.IsEnabled)
        {
            return;
        }

        WpfTestHost.RunInSta(() =>
        {
            var scenarios = new[]
            {
                new PageVisualReviewScenario("empty", 1d),
                new PageVisualReviewScenario("empty", 1.5d),
                new PageVisualReviewScenario("editor", 1d, OpenEditor),
                new PageVisualReviewScenario("editor", 1.5d, OpenEditor)
            };

            PageVisualReviewHarness.GenerateAndVerifyRepeatable(
                LocateRepositoryRoot(),
                "tts-rules",
                scenarios,
                CreateVisualReviewPage);
        });
    }

    private static void OpenEditor(FrameworkElement element)
    {
        ((TtsRulesViewLayoutContext)element.DataContext).HasEditor = true;
    }

    private static PageVisualReviewPage CreateVisualReviewPage()
    {
        var context = new TtsRulesViewLayoutContext
        {
            Rules =
            [
                new TtsRuleListItemViewModel(
                    1,
                    "标准云端语音",
                    "POST · https://speech.example.test/v1/audio",
                    true,
                    false,
                    false),
                new TtsRuleListItemViewModel(
                    2,
                    "本地调试服务",
                    "GET · http://127.0.0.1:5000/tts",
                    false,
                    false,
                    false),
                new TtsRuleListItemViewModel(
                    3,
                    "轻量备用规则",
                    "GET · https://backup.example.test/speak",
                    true,
                    false,
                    false)
            ]
        };
        context.HeaderEntries.Add(new EditableKeyValueItemViewModel("Accept", "audio/mpeg"));
        return new PageVisualReviewPage(
            new TtsRulesPage { DataContext = context },
            static () => { });
    }

    private static string LocateRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "src")) &&
                Directory.Exists(Path.Combine(current.FullName, "docs")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
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
