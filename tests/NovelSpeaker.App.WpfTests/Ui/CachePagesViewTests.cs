using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using CommunityToolkit.Mvvm.Input;
using NovelSpeaker.App.Shared.Presentation.Controls.Common;
using NovelSpeaker.App.Shared.Presentation.Controls.Feedback;
using Xunit;

namespace NovelSpeaker.App.WpfTests.Ui;

[Collection("WpfDispatcher")]
public sealed class CachePagesViewTests
{
    [Fact]
    public void Cache_page_projection_contracts_cover_actions_loading_completeness_and_export_surface()
    {
        CacheManagementPage_disables_both_top_actions_with_no_chapter_selection();
        CacheManagementPage_hides_chapter_workspace_while_chapters_are_loading();
        CacheManagementPage_projects_zero_and_unavailable_completeness_without_row_actions();
        CacheManagementPage_keeps_export_progress_out_of_page_and_exposes_header_icons_and_chapter_tooltip_contracts();
    }

    [Fact]
    public void Cache_page_layout_contracts_cover_independent_columns_and_workspace_height()
    {
        CacheManagementPage_constrains_workspace_and_scrolls_both_columns_independently();
    }

    private void CacheManagementPage_disables_both_top_actions_with_no_chapter_selection()
    {
        WpfTestHost.RunInSta(() =>
        {
            var provider = WpfTestHost.BuildServiceProvider();
            try
            {
                var page = provider.GetRequiredService<CacheManagementPage>();
                page.Measure(new Size(1200, 800));
                page.Arrange(new Rect(0, 0, 1200, 800));
                page.UpdateLayout();

                var clearButton = Assert.IsType<Wpf.Ui.Controls.Button>(page.FindName("ClearSelectedChaptersButton"));
                var exportButton = Assert.IsType<Wpf.Ui.Controls.Button>(page.FindName("ExportSelectedChaptersButton"));

                Assert.False(clearButton.IsEnabled);
                Assert.False(exportButton.IsEnabled);
                Assert.Equal("清理所选章节缓存", AutomationProperties.GetName(clearButton));
                Assert.Equal("导出所选章节", AutomationProperties.GetName(exportButton));
            }
            finally
            {
                provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
    }

    private void CacheManagementPage_constrains_workspace_and_scrolls_both_columns_independently()
    {
        WpfTestHost.RunInSta(() =>
        {
            var provider = WpfTestHost.BuildServiceProvider();
            Window? window = null;
            try
            {
                var page = provider.GetRequiredService<CacheManagementPage>();
                var books = Enumerable.Range(0, 80)
                    .Select(index => new CachedBookListItemViewModel(
                        $"book-{index}",
                        $"第 {index + 1} 本书",
                        "测试作者",
                        "1 MB",
                        "已缓存 1 章"))
                    .ToArray();
                var chapters = Enumerable.Range(0, 80)
                    .Select(index => new CachedChapterListItemViewModel(
                        "book-0",
                        index,
                        $"第 {index + 1} 章",
                        $"测试章节 {index + 1}",
                        "1 MB",
                        "1 个缓存条目",
                        "完整度：1/1 段 · 100%",
                        isExportable: true,
                        exportAccessibilityText: "可导出",
                        exportToolTip: "当前配置缓存完整，可导出为 MP3。"))
                    .ToArray();
                chapters[0].IsSelected = true;
                page.DataContext = new
                {
                    Books = books,
                    Chapters = chapters,
                    ShowSelectionPrompt = false,
                    ShowSelectedBookEmptyState = false,
                    ShowSelectedBookContent = true,
                    SelectedBookTitle = "第一本书",
                    SelectedBookAuthor = "测试作者",
                    SelectedBookChapterCountText = "已缓存 80 章",
                    SelectedBookCacheSizeText = "80 MB",
                    ChapterSelectionSummary = "已选择 1 章",
                    ClearSelectedChaptersCommand = new RelayCommand(() => { }),
                    ExportSelectedChaptersCommand = new RelayCommand(
                        () => { },
                        () => false),
                    ExportCommandToolTip = "导出可用章节；不可导出章节将先请求确认",
                };

                var frame = new Frame
                {
                    NavigationUIVisibility = System.Windows.Navigation.NavigationUIVisibility.Hidden
                };
                window = new Window
                {
                    Width = 1280,
                    Height = 760,
                    Content = frame
                };
                WpfWindowHost.Show(window);
                frame.Navigate(page);
                page.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                window.UpdateLayout();

                var rootViewport = Assert.IsType<Border>(page.FindName("RootViewport"));
                var booksScrollViewer = Assert.IsType<ScrollViewer>(page.FindName("BooksScrollViewer"));
                var chaptersListBox = Assert.IsType<ListBox>(page.FindName("ChaptersListBox"));
                var chaptersScrollViewer = Assert.Single(
                    VisualTreeTestHelper.FindDescendants<ScrollViewer>(chaptersListBox));
                var textBlocks = VisualTreeTestHelper.FindDescendants<TextBlock>(page)
                    .Select(textBlock => textBlock.Text)
                    .Where(text => !string.IsNullOrWhiteSpace(text))
                    .Cast<string>()
                    .ToArray();
                var layoutSnapshot =
                    $"frame={frame.ActualHeight}, root={rootViewport.ActualHeight}, " +
                    $"booksViewport={booksScrollViewer.ViewportHeight}, booksScrollable={booksScrollViewer.ScrollableHeight}, " +
                    $"chaptersViewport={chaptersScrollViewer.ViewportHeight}, chaptersScrollable={chaptersScrollViewer.ScrollableHeight}";

                Assert.Equal(frame.ActualHeight, rootViewport.Height, 3);
                Assert.True(booksScrollViewer.ScrollableHeight > 0, layoutSnapshot);
                Assert.True(chaptersScrollViewer.ScrollableHeight > 0, layoutSnapshot);
                Assert.True(VirtualizingPanel.GetIsVirtualizing(chaptersListBox));
                Assert.Equal(
                    VirtualizationMode.Recycling,
                    VirtualizingPanel.GetVirtualizationMode(chaptersListBox));
                Assert.True(ScrollViewer.GetCanContentScroll(chaptersListBox));
                Assert.Equal(ScrollUnit.Pixel, VirtualizingPanel.GetScrollUnit(chaptersListBox));
                Assert.True(
                    VisualTreeTestHelper.FindDescendants<ListBoxItem>(chaptersListBox).Count() < chapters.Length,
                    "章节列表应只创建可见项容器。");
                Assert.Same(chapters[0], Assert.Single(chaptersListBox.SelectedItems));
                Assert.Contains("书籍", textBlocks);
                Assert.DoesNotContain("有缓存的书籍", textBlocks);

                var firstBookCard = Assert.Single(
                    VisualTreeTestHelper.FindDescendants<Border>(booksScrollViewer),
                    border => AutomationProperties.GetName(border) == books[0].AutomationName);
                var firstBookButton = Assert.Single(VisualTreeTestHelper.FindDescendants<Button>(firstBookCard));
                Assert.Same(page.FindResource("App.Selection.CardItem"), firstBookCard.Style);
                Assert.Equal(new Thickness(1), firstBookCard.BorderThickness);
                Assert.InRange(Math.Abs(firstBookButton.ActualWidth - firstBookCard.ActualWidth), 0d, 2d);
                Assert.InRange(Math.Abs(firstBookButton.ActualHeight - firstBookCard.ActualHeight), 0d, 2d);

                var chapterCleanupButtons = VisualTreeTestHelper.FindDescendants<Button>(chaptersListBox)
                    .Where(button => AutomationProperties.GetName(button).StartsWith("清理第 ", StringComparison.Ordinal))
                    .ToArray();
                Assert.Empty(chapterCleanupButtons);

                var clearButton = Assert.IsType<Wpf.Ui.Controls.Button>(page.FindName("ClearSelectedChaptersButton"));
                var exportButton = Assert.IsType<Wpf.Ui.Controls.Button>(page.FindName("ExportSelectedChaptersButton"));
                var clearIcon = Assert.IsType<Wpf.Ui.Controls.SymbolIcon>(clearButton.Icon);
                var exportIcon = Assert.IsType<Wpf.Ui.Controls.SymbolIcon>(exportButton.Icon);
                Assert.Equal(Wpf.Ui.Controls.SymbolRegular.Delete24, clearIcon.Symbol);
                Assert.Equal("清理所选章节缓存", AutomationProperties.GetName(clearButton));
                Assert.Equal(Wpf.Ui.Controls.SymbolRegular.ArrowDownload24, exportIcon.Symbol);
                Assert.Equal("导出所选章节", AutomationProperties.GetName(exportButton));
                Assert.Equal("导出可用章节；不可导出章节将先请求确认", exportButton.ToolTip);
                Assert.False(exportButton.IsEnabled);
                Assert.DoesNotContain(
                    VisualTreeTestHelper.FindDescendants<Button>(page),
                    button => Equals(button.Content, "清理全部缓存"));

                var firstChapterButton = Assert.Single(
                    VisualTreeTestHelper.FindDescendants<Button>(chaptersListBox),
                    button => AutomationProperties.GetName(button) == chapters[0].AutomationName);
                var visibleChapterButtons = VisualTreeTestHelper.FindDescendants<Button>(chaptersListBox)
                    .Where(button => chapters.Any(chapter => AutomationProperties.GetName(button) == chapter.AutomationName))
                    .ToArray();
                Assert.True(firstChapterButton.ActualWidth > chaptersListBox.ActualWidth - 48d);
                Assert.All(
                    visibleChapterButtons,
                    button => Assert.InRange(Math.Abs(button.ActualWidth - firstChapterButton.ActualWidth), 0d, 2d));
                var chaptersSurface = Assert.IsType<AppSectionSurface>(page.FindName("ChaptersSurface"));
                Assert.True(string.IsNullOrWhiteSpace(chaptersSurface.Header));
                Assert.True(string.IsNullOrWhiteSpace(chaptersSurface.Description));
                var chapterContextMenu = Assert.IsType<ContextMenu>(firstChapterButton.ContextMenu);
                Assert.Same(page.FindResource("App.Menu.ContextSurface"), chapterContextMenu.Style);
                Assert.Equal(
                    ["清理所选章节", "导出所选章节"],
                    chapterContextMenu.Items.OfType<MenuItem>().Select(item => item.Header));
                var selectedBorder = Assert.Single(
                    VisualTreeTestHelper.FindDescendants<Border>(firstChapterButton),
                    border => border.Padding == new Thickness(16));
                Assert.NotEqual(System.Windows.Media.Brushes.Transparent, selectedBorder.Background);
                var firstChapterTexts = VisualTreeTestHelper.FindDescendants<TextBlock>(firstChapterButton).ToArray();
                var orderText = Assert.Single(firstChapterTexts, text => text.Text == chapters[0].OrderText);
                var titleText = Assert.Single(firstChapterTexts, text => text.Text == chapters[0].Title);
                Assert.Same(
                    System.Windows.Media.VisualTreeHelper.GetParent(orderText),
                    System.Windows.Media.VisualTreeHelper.GetParent(titleText));
                Assert.DoesNotContain(firstChapterTexts, text => text.Text == "已选择");
                Assert.DoesNotContain(firstChapterTexts, text => text.Text == chapters[0].ExportAccessibilityText);
            }
            finally
            {
                window?.Close();
                provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
    }

    private void CacheManagementPage_hides_chapter_workspace_while_chapters_are_loading()
    {
        WpfTestHost.RunInSta(() =>
        {
            var provider = WpfTestHost.BuildServiceProvider();
            try
            {
                var page = provider.GetRequiredService<CacheManagementPage>();
                page.DataContext = new
                {
                    ShowSelectionPrompt = false,
                    ShowSelectedBookEmptyState = false,
                    ShowSelectedBookContent = true,
                    IsLoadingChapters = true
                };

                page.Measure(new Size(1200, 800));
                page.Arrange(new Rect(0, 0, 1200, 800));
                page.UpdateLayout();

                var workspace = Assert.IsType<Grid>(page.FindName("SelectedBookWorkspace"));
                var loadingStatus = Assert.IsType<AppStatusView>(page.FindName("ChaptersLoadingStatusView"));
                Assert.Equal(Visibility.Collapsed, workspace.Visibility);
                Assert.Equal(Visibility.Visible, loadingStatus.Visibility);
            }
            finally
            {
                provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
    }

    private void CacheManagementPage_projects_zero_and_unavailable_completeness_without_row_actions()
    {
        WpfTestHost.RunInSta(() =>
        {
            var provider = WpfTestHost.BuildServiceProvider();
            try
            {
                var page = provider.GetRequiredService<CacheManagementPage>();
                var chapters = new[]
                {
                    new CachedChapterListItemViewModel(
                        "book-1", 0, "第 1 章", "零进度", "1 KB", "1 条缓存", "完整度：0/8 段 · 0%"),
                    new CachedChapterListItemViewModel(
                        "book-1", 1, "第 2 章", "计划计算", "1 KB", "1 条缓存", "完整度：计划计算中"),
                    new CachedChapterListItemViewModel(
                        "book-1", 2, "第 3 章", "配置不可用", "1 KB", "1 条缓存", "完整度：配置不可用")
                };
                chapters[0].IsSelected = true;
                page.DataContext = new
                {
                    Books = new[]
                    {
                        new CachedBookListItemViewModel("book-1", "第一本书", "作者甲", "3 KB", "已缓存 3 章")
                    },
                    Chapters = chapters,
                    ShowSelectionPrompt = false,
                    ShowSelectedBookEmptyState = false,
                    ShowSelectedBookContent = true,
                    SelectedBookTitle = "第一本书",
                    SelectedBookAuthor = "作者甲",
                    SelectedBookChapterCountText = "已缓存 3 章",
                    SelectedBookCacheSizeText = "3 KB",
                    ChapterSelectionSummary = "已选择 1 章",
                    ClearSelectedChaptersCommand = new RelayCommand(() => { }),
                    ExportSelectedChaptersCommand = new RelayCommand(() => { }),
                    ExportCommandToolTip = "将所选章节导出为 MP3",
                };

                page.Measure(new Size(1200, 800));
                page.Arrange(new Rect(0, 0, 1200, 800));
                page.UpdateLayout();

                var chapterList = Assert.IsType<ListBox>(page.FindName("ChaptersListBox"));
                var chapterTexts = VisualTreeTestHelper.FindDescendants<TextBlock>(chapterList)
                    .Select(textBlock => textBlock.Text)
                    .ToArray();
                Assert.Contains("完整度：0/8 段 · 0%", chapterTexts);
                Assert.Contains("完整度：计划计算中", chapterTexts);
                Assert.Contains("完整度：配置不可用", chapterTexts);
                Assert.DoesNotContain(
                    VisualTreeTestHelper.FindDescendants<Button>(chapterList),
                    button => AutomationProperties.GetName(button).StartsWith("清理第 ", StringComparison.Ordinal));
            }
            finally
            {
                provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
    }

    private void CacheManagementPage_keeps_export_progress_out_of_page_and_exposes_header_icons_and_chapter_tooltip_contracts()
    {
        WpfTestHost.RunInSta(() =>
        {
            var provider = WpfTestHost.BuildServiceProvider();
            try
            {
                var page = provider.GetRequiredService<CacheManagementPage>();
                var chapter = new CachedChapterListItemViewModel(
                    "book-1",
                    0,
                    "第 1 章",
                    "测试章节",
                    "1 KB",
                    "1 条缓存",
                    "完整度：0/1 段 · 0%",
                    isExportable: false,
                    exportAccessibilityText: "缓存不完整，无法导出",
                    exportToolTip: "当前配置缓存为 0/1 段，请先完成缓存。");
                page.DataContext = new
                {
                    Books = Array.Empty<CachedBookListItemViewModel>(),
                    Chapters = new[] { chapter },
                    ShowSelectionPrompt = false,
                    ShowSelectedBookEmptyState = false,
                    ShowSelectedBookContent = true,
                    SelectedBookTitle = "第一本书",
                    SelectedBookAuthor = "测试作者",
                    SelectedBookChapterCountText = "已缓存 1 章",
                    SelectedBookCacheSizeText = "1 KB",
                    ChapterSelectionSummary = "已选择 1 章",
                    ClearSelectedChaptersCommand = new RelayCommand(() => { }),
                    ExportSelectedChaptersCommand = new RelayCommand(() => { }),
                    ExportCommandToolTip = "所选章节缓存不完整，无法导出"
                };

                page.Measure(new Size(1200, 800));
                page.Arrange(new Rect(0, 0, 1200, 800));
                page.UpdateLayout();

                var clearButton = Assert.IsType<Wpf.Ui.Controls.Button>(page.FindName("ClearSelectedChaptersButton"));
                var exportButton = Assert.IsType<Wpf.Ui.Controls.Button>(page.FindName("ExportSelectedChaptersButton"));
                var chapterButton = Assert.Single(
                    VisualTreeTestHelper.FindDescendants<Button>(page),
                    button => AutomationProperties.GetName(button) == chapter.AutomationName);

                Assert.IsType<Wpf.Ui.Controls.SymbolIcon>(clearButton.Icon);
                Assert.IsType<Wpf.Ui.Controls.SymbolIcon>(exportButton.Icon);
                Assert.Null(page.FindName("ExportProgressPanel"));
                Assert.Null(page.FindName("CancelExportButton"));
                Assert.Null(page.FindName("OpenExportDirectoryButton"));
                Assert.Equal(chapter.ExportToolTip, chapterButton.ToolTip);
                Assert.Contains("缓存不完整，无法导出", chapter.AutomationName, StringComparison.Ordinal);
                Assert.DoesNotContain(
                    VisualTreeTestHelper.FindDescendants<TextBlock>(chapterButton),
                    text => text.Text == chapter.ExportAccessibilityText);
            }
            finally
            {
                provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
    }
}
