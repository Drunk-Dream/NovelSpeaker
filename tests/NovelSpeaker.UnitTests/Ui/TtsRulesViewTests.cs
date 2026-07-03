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
            Assert.Equal(Visibility.Visible, helpDrawer.Visibility);
            Assert.Equal(Visibility.Visible, ((UIElement)helpDrawer.Parent).Visibility);

            context.IsHelpDrawerOpen = false;
            view.UpdateLayout();

            Assert.Equal(Visibility.Visible, helpDrawer.Visibility);
            Assert.Equal(Visibility.Collapsed, ((UIElement)helpDrawer.Parent).Visibility);
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

    private sealed partial class TtsRulesViewLayoutContext : ObservableObject
    {
        public ObservableCollection<TtsRuleListItemViewModel> Rules { get; init; } = [];

        public ObservableCollection<EditableKeyValueItemViewModel> HeaderEntries { get; } = [];

        public ObservableCollection<EditableKeyValueItemViewModel> LoginInfoEntries { get; } = [];

        public ObservableCollection<EditableKeyValueItemViewModel> RequestHeaderEntries { get; } = [];

        public string StatusMessage { get; init; } = "状态";

        public string TestStatusMessage { get; init; } = "试听说明";

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

        public bool DraftEnabledCookieJar { get; init; }

        public string DraftTimeoutMs { get; init; } = "5000";

        public string PreviewMethodText { get; init; } = "GET";

        public string PreviewUrlText { get; init; } = "https://example.com/tts";

        public string PreviewHeadersText { get; init; } = "无";

        public string PreviewBodyText { get; init; } = "无";

        public string PreviewDeclaredContentTypeText { get; init; } = "audio/mpeg";

        public string PreviewWarningsText { get; init; } = "无";

        public string LastResponseStatusText { get; init; } = "尚未执行试听。";

        public string LastResponseDetailText { get; init; } = string.Empty;

        public bool CanSaveDraft => true;

        public bool CanCancelEditing => true;

        public bool CanDeleteCurrentRule => true;

        public bool CanSetCurrentRule => true;

        public bool CanClearRuleCookies => true;

        public bool CanExportDraft => true;

        public bool CanTestDraft => true;
    }
}
