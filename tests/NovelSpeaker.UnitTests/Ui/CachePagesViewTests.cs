using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using NovelSpeaker.App.Pages;
using NovelSpeaker.App.ViewModels;
using Xunit;

namespace NovelSpeaker.UnitTests.Ui;

public sealed class CachePagesViewTests
{
    [Fact]
    public void CacheAndDataPage_does_not_show_clear_all_button()
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

                var buttons = FindVisualChildren<System.Windows.Controls.Button>(page)
                    .Select(button => button.Content?.ToString())
                    .Where(text => !string.IsNullOrWhiteSpace(text))
                    .Cast<string>()
                    .ToArray();

                Assert.Contains("进入缓存管理", buttons);
                Assert.DoesNotContain("清理全部缓存", buttons);
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
                    SelectedBookCacheSizeText = "80 MB"
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
                var layoutSnapshot =
                    $"frame={frame.ActualHeight}, root={rootViewport.ActualHeight}, " +
                    $"booksViewport={booksScrollViewer.ViewportHeight}, booksScrollable={booksScrollViewer.ScrollableHeight}, " +
                    $"chaptersViewport={chaptersScrollViewer.ViewportHeight}, chaptersScrollable={chaptersScrollViewer.ScrollableHeight}";

                Assert.Equal(frame.ActualHeight, rootViewport.Height, 3);
                Assert.True(booksScrollViewer.ScrollableHeight > 0, layoutSnapshot);
                Assert.True(chaptersScrollViewer.ScrollableHeight > 0, layoutSnapshot);
            }
            finally
            {
                window?.Close();
                provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
    }

    private static IReadOnlyList<T> FindVisualChildren<T>(DependencyObject root)
        where T : DependencyObject
    {
        var results = new List<T>();
        Visit(root, results);
        return results;
    }

    private static void Visit<T>(DependencyObject node, List<T> results)
        where T : DependencyObject
    {
        for (var childIndex = 0; childIndex < System.Windows.Media.VisualTreeHelper.GetChildrenCount(node); childIndex++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(node, childIndex);
            if (child is T typedChild)
            {
                results.Add(typedChild);
            }

            Visit(child, results);
        }
    }
}
