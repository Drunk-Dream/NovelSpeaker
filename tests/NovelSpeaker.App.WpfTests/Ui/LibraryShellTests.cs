using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using NovelSpeaker.Application.Books;
using NovelSpeaker.App.Features.Books.Library;
using NovelSpeaker.App.Shell;
using NovelSpeaker.App.Shell.Navigation;
using Wpf.Ui.Controls;
using Xunit;

namespace NovelSpeaker.App.WpfTests.Ui;

[Collection("WpfDispatcher")]
public sealed class LibraryShellTests
{
    [Theory]
    [InlineData(25)]
    [InlineData(125)]
    public async Task Shell_library_keeps_a_finite_virtualized_viewport(int bookCount)
    {
        await WpfTestHost.RunInStaAsync(async () =>
        {
            await using var provider = await WpfTestHost.BuildInitializedServiceProviderAsync();
            await SeedBooksAsync(provider, bookCount);
            var window = provider.GetRequiredService<MainWindow>();
            using var host = WpfWindowHost.Show(window);
            await SettleAsync(window);
            var navigation = Assert.IsType<NavigationView>(window.FindName("RootNavigationView"));
            var presenter = Assert.IsType<NavigationViewContentPresenter>(VisualTreeTestHelper.FindDescendant<NavigationViewContentPresenter>(navigation));
            var page = Assert.IsType<LibraryPage>(VisualTreeTestHelper.FindDescendant<LibraryPage>(navigation));
            await page.ViewModel.LoadAsync(CancellationToken.None);
            await SettleAsync(window);
            Assert.Equal(bookCount, page.ViewModel.Books.Count);
            foreach (var size in new[] { new Size(900, 640), new Size(1280, 820), new Size(1450, 1000), new Size(900, 640) })
            {
                window.Width = size.Width;
                window.Height = size.Height;
                await SettleAsync(window);
                var items = Assert.IsType<ListBox>(page.FindName("BooksItemsControl"));
                var scroll = Assert.IsAssignableFrom<ScrollViewer>(VisualTreeTestHelper.FindDescendant<ScrollViewer>(items));
                var panel = Assert.IsType<VirtualizingStackPanel>(VisualTreeTestHelper.FindDescendant<VirtualizingStackPanel>(items));
                Assert.True(page.ActualHeight > 0 && page.ActualHeight <= presenter.ActualHeight + 1);
                Assert.True(scroll.ScrollableHeight > 0);
                Assert.InRange(panel.Children.Count, 1, bookCount >= 100 ? items.Items.Count / 2 : items.Items.Count);
                foreach (var fraction in new[] { 0.5, 1.0 })
                {
                    scroll.ScrollToVerticalOffset(scroll.ScrollableHeight * fraction);
                    await SettleAsync(window);
                    Assert.True(scroll.VerticalOffset > 0);
                    Assert.InRange(panel.Children.Count, 1, bookCount >= 100 ? items.Items.Count / 2 : items.Items.Count);
                }
            }
            // Ordinary settings still rely on the navigation host's content scrolling.
            Assert.True(await provider.GetRequiredService<IAppNavigator>().NavigateAsync(AppRoutes.Settings, CancellationToken.None));
            await SettleAsync(window);
            Assert.True(presenter.IsDynamicScrollViewerEnabled);
            var outer = VisualTreeTestHelper.FindDescendants<ScrollViewer>(presenter)
                .First(scroll => scroll.ScrollableHeight > 0);
            outer.ScrollToEnd();
            await SettleAsync(window);
            Assert.True(outer.VerticalOffset > 0);
        });
    }

    private static async Task SeedBooksAsync(IServiceProvider provider, int count)
    {
        var directory = Path.Combine(Path.GetTempPath(), "NovelSpeaker-LibraryShell-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var importer = provider.GetRequiredService<IDirectBookImportService>();
            var path = Path.Combine(directory, "fiction.txt");
            for (var index = 0; index < count; index++)
            {
                await File.WriteAllTextAsync(path, $"第一章 虚构章节\n这是虚构测试正文 {index}。\n");
                var result = await importer.ImportAsync(new DirectBookImportRequest(path, "utf-8", "fiction.txt",
                    new BookImportIdentity($"虚构书籍 {index:D4}", "虚构作者")), null, CancellationToken.None);
                Assert.Equal(DirectBookImportStatus.Imported, result.Status);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task SettleAsync(Window window)
    {
        window.UpdateLayout();
        await WpfTestHost.DrainDispatcherAsync();
        window.UpdateLayout();
    }
}
