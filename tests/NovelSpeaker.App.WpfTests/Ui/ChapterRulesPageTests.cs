using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using NovelSpeaker.App.Shared.Presentation.Controls.Common;
using NovelSpeaker.App.Shared.Presentation.Controls.Forms;
using NovelSpeaker.App.Shared.Presentation.Rules;
using NovelSpeaker.App.Shared.Theming;
using SymbolIcon = Wpf.Ui.Controls.SymbolIcon;
using SymbolRegular = Wpf.Ui.Controls.SymbolRegular;
using Xunit;

namespace NovelSpeaker.App.WpfTests.Ui;

[Collection("WpfDispatcher")]
public sealed partial class ChapterRulesPageTests
{
    [Fact]
    public void ChapterRulesPage_uses_formal_split_workbench_and_shared_sortable_cards()
    {
        WpfTestHost.RunInSta(() =>
        {
            var rule = CreateRule("custom:one", "章节数字", true, false, true);
            var view = CreateView(new ChapterRulesViewLayoutContext
            {
                HasEditor = true,
                Rules = [rule]
            });

            var header = Assert.IsType<AppPageHeader>(view.FindName("PageHeader"));
            Assert.Equal("章节规则", header.Title);
            Assert.Empty(header.Description);
            Assert.NotNull(header.Actions);
            Assert.IsType<AppSectionSurface>(view.FindName("RulesSurface"));
            Assert.IsType<AppSectionSurface>(view.FindName("EditorSurface"));
            Assert.Equal(2, VisualTreeTestHelper.FindDescendants<AppFormField>(view).Count());
            Assert.Null(VisualTreeTestHelper.FindDescendant<DataGrid>(view));

            var item = Assert.IsType<RuleListItemView>(VisualTreeTestHelper.FindDescendant<RuleListItemView>(
                view,
                candidate => ReferenceEquals(candidate.CommandParameter, rule)));
            Assert.Equal(rule.Name, item.Title);
            Assert.Equal(rule.PatternSummary, item.Summary);
            Assert.True(item.IsSortable);
            Assert.True(item.IsRuleEnabled);
            Assert.True(item.IsSelected);
        });
    }

    [Fact]
    public void ChapterRulesPage_toolbar_contains_distinct_default_actions_and_help_without_export()
    {
        WpfTestHost.RunInSta(() =>
        {
            var view = CreateView(new ChapterRulesViewLayoutContext());

            AssertToolbarIcon(view, "新建规则", SymbolRegular.DocumentAdd24);
            AssertToolbarIcon(view, "从文件导入", SymbolRegular.ArrowImport24);
            AssertToolbarIcon(view, "从剪切板导入", SymbolRegular.ClipboardPaste24);
            AssertToolbarIcon(view, "导入默认规则", SymbolRegular.ArrowDownload24);
            AssertToolbarIcon(view, "恢复默认规则", SymbolRegular.ArrowReset24);
            AssertToolbarIcon(view, "章节规则帮助", SymbolRegular.QuestionCircle24);
            Assert.DoesNotContain(
                VisualTreeTestHelper.FindDescendants<Button>(view),
                button => AutomationProperties.GetName(button).Contains("导出", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void ChapterRulesPage_toolbar_icons_follow_dark_theme_foreground()
    {
        WpfTestHost.RunInSta(() =>
        {
            var runtime = new WpfUiThemeRuntime();
            runtime.ApplyDarkTheme();
            try
            {
                var view = CreateView(new ChapterRulesViewLayoutContext());
                var expected = Assert.IsAssignableFrom<Brush>(view.FindResource("App.Brush.Text.Primary"));

                foreach (var name in new[]
                         {
                             "新建规则", "从文件导入", "从剪切板导入",
                             "导入默认规则", "恢复默认规则", "章节规则帮助"
                         })
                {
                    var button = Assert.Single(VisualTreeTestHelper.FindDescendants<Button>(
                        view,
                        candidate => AutomationProperties.GetName(candidate) == name));
                    var icon = Assert.IsType<SymbolIcon>(VisualTreeTestHelper.FindDescendant<SymbolIcon>(button));
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
    public void ChapterRulesPage_context_menu_exposes_export_copy_move_and_capability_delete_without_ellipsis()
    {
        WpfTestHost.RunInSta(() =>
        {
            var custom = CreateRule("custom:one", "自定义规则", true, false, true);
            custom.CanMoveUp = false;
            custom.CanMoveDown = true;
            var builtIn = CreateRule("builtin:two", "内置规则", true, true, false);
            var view = CreateView(new ChapterRulesViewLayoutContext { Rules = [custom, builtIn] });

            var customItem = FindRuleItem(view, custom);
            customItem.ContextMenu!.PlacementTarget = customItem;
            customItem.ContextMenu.IsOpen = true;
            var menuItems = customItem.ContextMenu.Items.OfType<MenuItem>().ToArray();
            Assert.Equal(
                ["导出到文件", "复制到剪切板", "上移", "下移", "删除"],
                menuItems.Where(item => item.Visibility == Visibility.Visible).Select(item => item.Header));
            Assert.False(menuItems.Single(item => Equals(item.Header, "上移")).IsEnabled);
            Assert.True(menuItems.Single(item => Equals(item.Header, "下移")).IsEnabled);
            Assert.True(menuItems.Single(item => Equals(item.Header, "删除")).IsEnabled);
            customItem.ContextMenu.IsOpen = false;

            var builtInItem = FindRuleItem(view, builtIn);
            builtInItem.ContextMenu!.PlacementTarget = builtInItem;
            builtInItem.ContextMenu.IsOpen = true;
            Assert.False(builtInItem.ContextMenu.Items
                .OfType<MenuItem>()
                .Single(item => Equals(item.Header, "删除"))
                .IsEnabled);
            builtInItem.ContextMenu.IsOpen = false;
            Assert.DoesNotContain(
                VisualTreeTestHelper.FindDescendants<Button>(view),
                button => AutomationProperties.GetName(button).Contains("更多操作", StringComparison.Ordinal));
            Assert.DoesNotContain(
                VisualTreeTestHelper.FindDescendants<FrameworkElement>(view),
                element => AutomationProperties.GetName(element).Contains("拖动排序", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void ChapterRulesPage_projects_validation_through_form_fields_and_keeps_cancel_save_actions()
    {
        WpfTestHost.RunInSta(() =>
        {
            var view = CreateView(new ChapterRulesViewLayoutContext
            {
                HasEditor = true,
                CanCancelEditing = true,
                CanSaveDraft = false,
                NameValidationMessage = "名称不能为空。",
                PatternValidationMessage = "正则表达式无效。"
            });

            var fields = VisualTreeTestHelper.FindDescendants<AppFormField>(view).ToArray();
            Assert.Equal(["名称", "正则表达式"], fields.Select(field => field.Label));
            Assert.Equal("名称不能为空。", fields[0].Error);
            Assert.Equal("正则表达式无效。", fields[1].Error);
            Assert.True(Assert.Single(VisualTreeTestHelper.FindDescendants<Button>(
                view,
                button => Equals(button.Content, "取消"))).IsEnabled);
            Assert.False(Assert.Single(VisualTreeTestHelper.FindDescendants<Button>(
                view,
                button => Equals(button.Content, "保存"))).IsEnabled);
            Assert.Null(VisualTreeTestHelper.FindDescendant<Button>(
                view,
                button => Equals(button.Content, "删除")));
        });
    }

    [Fact]
    public void ChapterRulesPage_keeps_virtualized_list_and_editor_visible_at_minimum_width()
    {
        WpfTestHost.RunInSta(() =>
        {
            var rules = new ObservableCollection<ChapterRuleListItemViewModel>();
            for (var index = 0; index < 40; index++)
            {
                rules.Add(CreateRule($"custom:{index}", $"规则 {index}", index % 2 == 0, false, index == 0));
            }

            var view = CreateView(new ChapterRulesViewLayoutContext { HasEditor = true, Rules = rules }, 900, 640);
            var list = Assert.IsType<ListBox>(view.FindName("RulesList"));
            var listScrollViewer = Assert.IsAssignableFrom<ScrollViewer>(
                VisualTreeTestHelper.FindDescendant<ScrollViewer>(list));
            var editorScrollViewer = Assert.IsType<ScrollViewer>(view.FindName("RuleEditorScrollViewer"));

            Assert.True(listScrollViewer.ActualWidth > 0);
            Assert.True(listScrollViewer.ScrollableHeight > 0);
            Assert.True(editorScrollViewer.ActualWidth > 0);
            Assert.True(System.Windows.Controls.VirtualizingPanel.GetIsVirtualizing(list));
            Assert.Equal(VirtualizationMode.Recycling, VirtualizingPanel.GetVirtualizationMode(list));
        });
    }

    [Fact]
    public void ChapterRulesPage_help_drawer_explains_matching_and_sorting()
    {
        WpfTestHost.RunInSta(() =>
        {
            var context = new ChapterRulesViewLayoutContext { IsHelpDrawerOpen = true };
            var view = CreateView(context, 1000, 700);
            var helpDrawer = Assert.IsType<Border>(view.FindName("HelpDrawerBorder"));
            Assert.Equal(Visibility.Visible, ((UIElement)helpDrawer.Parent).Visibility);
            Assert.NotEqual(Brushes.Transparent, helpDrawer.Background);

            var texts = VisualTreeTestHelper.FindDescendants<TextBlock>(view)
                .Select(textBlock => textBlock.Text)
                .ToHashSet(StringComparer.Ordinal);
            Assert.Contains("规则如何生效", texts);
            Assert.Contains("从一个规则开始", texts);
            Assert.Contains(@"^\s*第[0-9一二三四五六七八九十百千零两]+章(?:\s+.+)?\s*$", texts);
            Assert.Contains("排序与误识别", texts);

            context.IsHelpDrawerOpen = false;
            view.UpdateLayout();
            Assert.Equal(Visibility.Collapsed, ((UIElement)helpDrawer.Parent).Visibility);
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

    [Fact]
    public void Chapter_rules_visual_review_generates_stable_page_screenshots()
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
                "chapter-rules",
                scenarios,
                CreateVisualReviewPage);
        });
    }

    private static ChapterRulesPage CreateView(
        ChapterRulesViewLayoutContext context,
        double width = 1280,
        double height = 760)
    {
        var view = new ChapterRulesPage { DataContext = context };
        view.Measure(new Size(width, height));
        view.Arrange(new Rect(0, 0, width, height));
        view.UpdateLayout();
        return view;
    }

    private static ChapterRuleListItemViewModel CreateRule(
        string id,
        string name,
        bool isEnabled,
        bool isBuiltIn,
        bool isSelected) =>
        new(id, name, @"^\s*第[0-9一二三四五六七八九十]+章\s*$", isEnabled, isBuiltIn, isSelected);

    private static RuleListItemView FindRuleItem(ChapterRulesPage view, ChapterRuleListItemViewModel rule) =>
        Assert.IsType<RuleListItemView>(VisualTreeTestHelper.FindDescendant<RuleListItemView>(
            view,
            candidate => ReferenceEquals(candidate.CommandParameter, rule)));

    private static void AssertToolbarIcon(
        ChapterRulesPage view,
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

    private static void OpenEditor(FrameworkElement element) =>
        ((ChapterRulesViewLayoutContext)element.DataContext).HasEditor = true;

    private static PageVisualReviewPage CreateVisualReviewPage()
    {
        var context = new ChapterRulesViewLayoutContext
        {
            DraftName = "中文章节标题",
            DraftPattern = @"^\s*第[0-9一二三四五六七八九十百千零两]+章(?:\s+.+)?\s*$",
            Rules =
            [
                CreateRule("builtin:number", "数字章节", true, true, true),
                CreateRule("builtin:volume", "卷标题", true, true, false),
                CreateRule("custom:extra", "番外与后记", false, false, false)
            ]
        };
        return new PageVisualReviewPage(new ChapterRulesPage { DataContext = context }, static () => { });
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

    private sealed partial class ChapterRulesViewLayoutContext : ObservableObject
    {
        public RelayCommand NewRuleCommand { get; } = new(static () => { });
        public RelayCommand ImportDefaultsCommand { get; } = new(static () => { });
        public RelayCommand RestoreDefaultsCommand { get; } = new(static () => { });
        public RelayCommand OpenHelpCommand { get; } = new(static () => { });
        public RelayCommand CloseHelpCommand { get; } = new(static () => { });
        public RelayCommand<object> SelectRuleCommand { get; } = new(static _ => { });
        public RelayCommand<object> ToggleRuleEnabledCommand { get; } = new(static _ => { });
        public RelayCommand<object> ExportRuleCommand { get; } = new(static _ => { });
        public RelayCommand<object> CopyRuleCommand { get; } = new(static _ => { });
        public RelayCommand<object> MoveRuleUpCommand { get; } = new(static _ => { });
        public RelayCommand<object> MoveRuleDownCommand { get; } = new(static _ => { });
        public RelayCommand<object> DeleteRuleCommand { get; } = new(static _ => { });
        public RelayCommand<object> ReorderRuleCommand { get; } = new(static _ => { });

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
