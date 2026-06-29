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
            var page = new BookDetailsPage(new FakeNavigationService())
            {
                DataContext = new BookDetailsNavigationRequest("book-42")
            };

            page.OnNavigatedToAsync().GetAwaiter().GetResult();

            Assert.Equal("book-42", page.LastRequest?.BookId);
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
                page.DataContext = new PlayerNavigationRequest("book-7");

                page.OnNavigatedToAsync().GetAwaiter().GetResult();

                Assert.Equal("book-7", page.LastRequest?.BookId);
            }
            finally
            {
                provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
    }

    private sealed class FakeNavigationService : Wpf.Ui.INavigationService
    {
        public Wpf.Ui.Controls.INavigationView GetNavigationControl()
        {
            throw new NotSupportedException();
        }

        public bool GoBack() => false;

        public bool Navigate(Type pageType) => true;

        public bool Navigate(Type pageType, object? dataContext) => true;

        public bool Navigate(string pageIdOrTargetTag) => true;

        public bool Navigate(string pageIdOrTargetTag, object? dataContext) => true;

        public bool NavigateWithHierarchy(Type pageType) => true;

        public bool NavigateWithHierarchy(Type pageType, object? dataContext) => true;

        public void SetNavigationControl(Wpf.Ui.Controls.INavigationView navigation)
        {
        }
    }
}
