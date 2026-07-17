using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using NovelSpeaker.App.Navigation;
using NovelSpeaker.App.Pages;
using Xunit;

namespace NovelSpeaker.UnitTests.Ui;

public sealed class NavigationViewportHeightBehaviorTests
{
    [Fact]
    public void Fixed_viewport_pages_use_shared_navigation_viewport_behavior()
    {
        WpfTestHost.RunInSta(() =>
        {
            var provider = WpfTestHost.BuildServiceProvider();
            try
            {
                Page[] pages =
                [
                    provider.GetRequiredService<PlayerPage>(),
                    provider.GetRequiredService<BookDetailsPage>(),
                    provider.GetRequiredService<TtsRulesPage>(),
                    provider.GetRequiredService<ChapterRulesPage>(),
                    provider.GetRequiredService<RegexReplacementRulesPage>(),
                    provider.GetRequiredService<CacheManagementPage>()
                ];

                foreach (var page in pages)
                {
                    var rootViewport = Assert.IsAssignableFrom<FrameworkElement>(page.FindName("RootViewport"));
                    Assert.True(
                        NavigationViewportHeightBehavior.GetIsEnabled(rootViewport),
                        $"{page.GetType().Name} must use the shared navigation viewport behavior.");
                }
            }
            finally
            {
                provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
    }

    [Fact]
    public void Enabled_element_tracks_navigation_frame_height_and_releases_it_when_unloaded()
    {
        WpfTestHost.RunInSta(() =>
        {
            var viewport = new Border();
            NavigationViewportHeightBehavior.SetIsEnabled(viewport, true);
            var page = new Page { Content = viewport };
            var frame = new Frame
            {
                NavigationUIVisibility = System.Windows.Navigation.NavigationUIVisibility.Hidden
            };
            var window = new Window
            {
                Width = 1100,
                Height = 700,
                Content = frame
            };

            try
            {
                window.Show();
                frame.Navigate(page);
                page.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                window.UpdateLayout();

                Assert.Equal(frame.ActualHeight, viewport.Height, 3);

                window.Height = 820;
                page.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                window.UpdateLayout();

                Assert.Equal(frame.ActualHeight, viewport.Height, 3);

                frame.Navigate(new Page());
                page.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

                Assert.True(double.IsNaN(viewport.Height));
            }
            finally
            {
                window.Close();
            }
        });
    }
}
