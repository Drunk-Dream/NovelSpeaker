using Microsoft.Extensions.DependencyInjection;
using NovelSpeaker.App.Navigation;
using NovelSpeaker.App.Pages;
using Wpf.Ui;
using Wpf.Ui.Controls;
using Xunit;

namespace NovelSpeaker.UnitTests.Navigation;

public sealed class AppNavigationPageProviderTests
{
    [Fact]
    public void GetPage_resolves_registered_page()
    {
        WpfTestHost.RunInSta(() =>
        {
            var services = new ServiceCollection();
            services.AddSingleton<INavigationService, FakeNavigationService>();
            services.AddTransient<BookDetailsPage>();

            using var provider = services.BuildServiceProvider();
            var pageProvider = new AppNavigationPageProvider(provider);

            var page = pageProvider.GetPage(typeof(BookDetailsPage));

            Assert.IsType<BookDetailsPage>(page);
        });
    }

    [Fact]
    public void GetPage_throws_for_unregistered_page()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var pageProvider = new AppNavigationPageProvider(provider);

        Assert.Throws<InvalidOperationException>(() => pageProvider.GetPage(typeof(UnregisteredPage)));
    }

    private sealed class UnregisteredPage : System.Windows.Controls.Page
    {
    }

    private sealed class FakeNavigationService : INavigationService
    {
        public INavigationView GetNavigationControl()
        {
            throw new NotSupportedException();
        }

        public bool GoBack()
        {
            return false;
        }

        public bool Navigate(Type pageType) => true;

        public bool Navigate(Type pageType, object? dataContext) => true;

        public bool Navigate(string pageIdOrTargetTag) => true;

        public bool Navigate(string pageIdOrTargetTag, object? dataContext) => true;

        public bool NavigateWithHierarchy(Type pageType) => true;

        public bool NavigateWithHierarchy(Type pageType, object? dataContext) => true;

        public void SetNavigationControl(INavigationView navigation)
        {
        }
    }
}
