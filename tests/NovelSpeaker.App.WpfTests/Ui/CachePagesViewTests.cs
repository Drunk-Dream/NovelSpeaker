using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using CommunityToolkit.Mvvm.Input;
using Xunit;

namespace NovelSpeaker.App.WpfTests.Ui;

[Collection("WpfDispatcher")]
public sealed class CachePagesViewTests
{
    [Fact]
    public void CacheManagementPage_disables_both_top_actions_with_no_chapter_selection()
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

                var clearButton = Assert.IsType<Button>(page.FindName("ClearSelectedChaptersButton"));
                var exportButton = Assert.IsType<Button>(page.FindName("ExportSelectedChaptersButton"));

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

    [Fact]
    public void CacheAndDataPage_keeps_clear_all_as_the_parent_level_dangerous_action()
    {
        WpfTestHost.RunInSta(() =>
        {
            var provider = WpfTestHost.BuildServiceProvider();
            try
            {
                var page = provider.GetRequiredService<CacheAndDataPage>();
                page.Measure(new Size(1200, 800));
                page.Arrange(new Rect(0, 0, 1200, 800));
                page.UpdateLayout();

                var buttons = VisualTreeTestHelper.FindDescendants<System.Windows.Controls.Button>(page).ToArray();

                Assert.Contains(buttons, button => AutomationProperties.GetName(button) == "缓存管理");
                var clearAllButton = Assert.Single(
                    buttons,
                    button => AutomationProperties.GetName(button) == "清理全部缓存");
                Assert.Equal("清理全部缓存", clearAllButton.Content);
                Assert.Equal("清理全部缓存", clearAllButton.ToolTip);
            }
            finally
            {
                provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
    }

    [Fact]
    public void CacheManagementPage_constrains_workspace_and_scrolls_both_columns_independently()
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
                        "1/1 段（100%）"))
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
                    CanExportSelectedChapters = false
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
                window.Show();
                frame.Navigate(page);
                page.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                window.UpdateLayout();

                var rootViewport = Assert.IsType<Border>(page.FindName("RootViewport"));
                var booksScrollViewer = Assert.IsType<ScrollViewer>(page.FindName("BooksScrollViewer"));
                var chaptersScrollViewer = Assert.IsType<ScrollViewer>(page.FindName("ChaptersScrollViewer"));
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
                Assert.Contains("书籍", textBlocks);
                Assert.DoesNotContain("有缓存的书籍", textBlocks);

                var firstBookCard = Assert.Single(
                    VisualTreeTestHelper.FindDescendants<Border>(booksScrollViewer),
                    border => AutomationProperties.GetName(border) == books[0].AutomationName);
                var firstBookButton = Assert.Single(VisualTreeTestHelper.FindDescendants<Button>(firstBookCard));
                Assert.InRange(Math.Abs(firstBookButton.ActualWidth - firstBookCard.ActualWidth), 0d, 1d);
                Assert.InRange(Math.Abs(firstBookButton.ActualHeight - firstBookCard.ActualHeight), 0d, 1d);

                var chapterCleanupButtons = VisualTreeTestHelper.FindDescendants<Button>(chaptersScrollViewer)
                    .Where(button => AutomationProperties.GetName(button).StartsWith("清理第 ", StringComparison.Ordinal))
                    .ToArray();
                Assert.Empty(chapterCleanupButtons);

                var clearButton = Assert.IsType<Button>(page.FindName("ClearSelectedChaptersButton"));
                var exportButton = Assert.IsType<Button>(page.FindName("ExportSelectedChaptersButton"));
                Assert.Equal("清理", clearButton.Content);
                Assert.Equal("清理所选章节缓存", AutomationProperties.GetName(clearButton));
                Assert.Equal("导出", exportButton.Content);
                Assert.Equal("导出所选章节", AutomationProperties.GetName(exportButton));
                Assert.Equal("章节导出尚未接入", exportButton.ToolTip);
                Assert.False(exportButton.IsEnabled);
                Assert.DoesNotContain(
                    VisualTreeTestHelper.FindDescendants<Button>(page),
                    button => Equals(button.Content, "清理全部缓存"));

                var firstChapterButton = Assert.Single(
                    VisualTreeTestHelper.FindDescendants<Button>(chaptersScrollViewer),
                    button => AutomationProperties.GetName(button) == chapters[0].AutomationName);
                var selectedBorder = Assert.Single(
                    VisualTreeTestHelper.FindDescendants<Border>(firstChapterButton),
                    border => border.Padding == new Thickness(16));
                Assert.NotEqual(System.Windows.Media.Brushes.Transparent, selectedBorder.Background);
                Assert.Contains(
                    VisualTreeTestHelper.FindDescendants<TextBlock>(firstChapterButton),
                    text => text.Text == "已选择" && text.Visibility == Visibility.Visible);
            }
            finally
            {
                window?.Close();
                provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
    }

}
