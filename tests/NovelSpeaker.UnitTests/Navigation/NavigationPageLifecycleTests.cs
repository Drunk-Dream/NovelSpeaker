using Microsoft.Extensions.DependencyInjection;
using NovelSpeaker.App.Navigation;
using NovelSpeaker.App.Pages;
using Xunit;

namespace NovelSpeaker.UnitTests.Navigation;

public sealed class NavigationPageLifecycleTests
{
    [Fact]
    public void BookDetailsPage_captures_strongly_typed_navigation_request()
    {
        WpfTestHost.RunInSta(() =>
        {
            var provider = WpfTestHost.BuildServiceProvider();
            try
            {
                var page = provider.GetRequiredService<BookDetailsPage>();
                page.DataContext = new BookDetailsNavigationRequest("book-42");

                page.OnNavigatedToAsync().GetAwaiter().GetResult();

                Assert.Equal("book-42", page.LastRequest?.BookId);
            }
            finally
            {
                provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
    }

    [Fact]
    public void PlayerPage_captures_strongly_typed_navigation_request()
    {
        WpfTestHost.RunInSta(() =>
        {
            var provider = WpfTestHost.BuildServiceProvider();
            try
            {
                var page = provider.GetRequiredService<PlayerPage>();
                typeof(PlayerPage)
                    .GetField("_hasLoaded", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?
                    .SetValue(page, true);
                page.DataContext = new PlayerNavigationRequest("book-7", PlayerNavigationMode.ReturnToCurrentSession);

                page.OnNavigatedToAsync().GetAwaiter().GetResult();

                Assert.Equal("book-7", page.LastRequest?.BookId);
                Assert.Equal(PlayerNavigationMode.ReturnToCurrentSession, page.LastRequest?.Mode);
            }
            finally
            {
                provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
    }
}
