using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using NovelSpeaker.App.ViewModels;
using NovelSpeaker.App.Views;
using Xunit;

namespace NovelSpeaker.UnitTests.Ui;

public sealed partial class TtsRulesViewTests
{
    [Fact]
    public void TtsRulesView_uses_split_scrollable_workspace_without_datagrid()
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

            var view = new TtsRulesView
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
    public void TtsRulesView_exposes_rule_item_automation_name()
    {
        WpfTestHost.RunInSta(() =>
        {
            var targetRule = new TtsRuleListItemViewModel(2, "当前规则", false, true, true);
            var view = new TtsRulesView
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

            var button = FindDescendant<Button>(
                view,
                candidate => AutomationProperties.GetName(candidate) == targetRule.AutomationName);

            Assert.NotNull(button);
            Assert.Equal("当前规则，已禁用，当前规则，已选中", AutomationProperties.GetName(button!));
        });
    }

    [Fact]
    public void TtsRulesView_toggles_help_drawer_visibility()
    {
        WpfTestHost.RunInSta(() =>
        {
            var context = new TtsRulesViewLayoutContext
            {
                IsHelpDrawerOpen = true
            };
            var view = new TtsRulesView
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
    public void TtsRulesView_help_drawer_contains_guided_get_and_post_examples()
    {
        WpfTestHost.RunInSta(() =>
        {
            var view = new TtsRulesView
            {
                DataContext = new TtsRulesViewLayoutContext
                {
                    IsHelpDrawerOpen = true
                }
            };

            view.Measure(new Size(1000, 700));
            view.Arrange(new Rect(0, 0, 1000, 700));
            view.UpdateLayout();

            var helpTexts = FindDescendants<TextBlock>(view, _ => true)
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
    public void TtsRulesView_keeps_both_panes_visible_at_minimum_supported_width()
    {
        WpfTestHost.RunInSta(() =>
        {
            var view = new TtsRulesView
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
    public void TtsRulesView_hides_removed_rule_controls_and_preview_area()
    {
        WpfTestHost.RunInSta(() =>
        {
            var view = new TtsRulesView
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

            Assert.Null(FindDescendant<TextBlock>(view, candidate => candidate.Text == "LoginInfo"));
            Assert.Null(FindDescendant<Expander>(view, candidate => Equals(candidate.Header, "高级设置")));
            Assert.Null(FindDescendant<TextBlock>(view, candidate => candidate.Text == "请求预览与结果"));
            Assert.Null(FindDescendant<Button>(view, candidate => Equals(candidate.Content, "生成预览")));
            Assert.Null(FindDescendant<Button>(view, candidate => Equals(candidate.Content, "取消试听")));
            Assert.Null(FindDescendant<Button>(view, candidate => Equals(candidate.Content, "清除 Cookie")));
            Assert.Null(FindDescendant<TextBlock>(view, candidate => candidate.Text == "超时 (ms)"));
        });
    }

    [Fact]
    public void TtsRulesView_shows_request_body_only_for_post()
    {
        WpfTestHost.RunInSta(() =>
        {
            var getView = new TtsRulesView
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

            var hiddenRequestBodyLabel = FindDescendant<TextBlock>(getView, candidate => candidate.Text == "请求体");
            Assert.NotNull(hiddenRequestBodyLabel);
            Assert.Equal(Visibility.Collapsed, ((FrameworkElement)hiddenRequestBodyLabel!.Parent).Visibility);

            var postView = new TtsRulesView
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

            var visibleRequestBodyLabel = FindDescendant<TextBlock>(postView, candidate => candidate.Text == "请求体");
            Assert.NotNull(visibleRequestBodyLabel);
            Assert.Equal(Visibility.Visible, ((FrameworkElement)visibleRequestBodyLabel!.Parent).Visibility);
        });
    }

    [Fact]
    public void TtsRulesView_shows_concurrent_rate_format_tooltip()
    {
        WpfTestHost.RunInSta(() =>
        {
            var view = new TtsRulesView
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

            var tooltipTextBox = FindDescendant<TextBox>(
                view,
                candidate => Equals(candidate.ToolTip, "格式：次数/毫秒，例如 2/1000"));

            Assert.NotNull(tooltipTextBox);
        });
    }

    private static T? FindDescendant<T>(DependencyObject root, Func<T, bool> predicate)
        where T : DependencyObject
    {
        for (var childIndex = 0; childIndex < VisualTreeHelper.GetChildrenCount(root); childIndex++)
        {
            var child = VisualTreeHelper.GetChild(root, childIndex);
            if (child is T typed && predicate(typed))
            {
                return typed;
            }

            var descendant = FindDescendant(child, predicate);
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
    }

    private static IEnumerable<T> FindDescendants<T>(DependencyObject root, Func<T, bool> predicate)
        where T : DependencyObject
    {
        for (var childIndex = 0; childIndex < VisualTreeHelper.GetChildrenCount(root); childIndex++)
        {
            var child = VisualTreeHelper.GetChild(root, childIndex);
            if (child is T typed && predicate(typed))
            {
                yield return typed;
            }

            foreach (var descendant in FindDescendants(child, predicate))
            {
                yield return descendant;
            }
        }
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

        public bool CanSaveDraft => true;

        public bool CanCancelEditing => true;

        public bool CanDeleteCurrentRule => true;

        public bool CanSetCurrentRule => true;

        public bool CanExportDraft => true;

        public bool CanTestDraft => true;

        public bool IsPostMethod => string.Equals(DraftRequestMethod, "POST", StringComparison.OrdinalIgnoreCase);
    }
}
