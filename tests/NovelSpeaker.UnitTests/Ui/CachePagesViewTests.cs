using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using NovelSpeaker.App.Pages;
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
    public void CacheManagementPage_exposes_separate_left_and_right_scroll_regions()
    {
        WpfTestHost.RunInSta(() =>
        {
            var provider = WpfTestHost.BuildServiceProvider();
            try
            {
                var page = provider.GetRequiredService<CacheManagementPage>();
                page.Measure(new Size(1280, 820));
                page.Arrange(new Rect(0, 0, 1280, 820));
                page.UpdateLayout();

                Assert.NotNull(page.FindName("BooksScrollViewer"));
                Assert.NotNull(page.FindName("ChaptersScrollViewer"));
            }
            finally
            {
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
