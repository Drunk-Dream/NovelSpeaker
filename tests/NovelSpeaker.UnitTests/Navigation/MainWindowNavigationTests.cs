using Microsoft.Extensions.DependencyInjection;
using NovelSpeaker.App;
using NovelSpeaker.App.Navigation;
using NovelSpeaker.App.Pages;
using NovelSpeaker.App.Theming;
using NovelSpeaker.App.ViewModels;
using Wpf.Ui;
using Wpf.Ui.Abstractions;
using Wpf.Ui.Controls;
using Xunit;

namespace NovelSpeaker.UnitTests.Navigation;

public sealed class MainWindowNavigationTests
{
    [Fact]
    public void Loaded_initializes_navigation_once_and_targets_library_page()
    {
        WpfTestHost.RunInSta(() =>
        {
            var navigationService = new FakeNavigationService();
            var pageProvider = new FakeNavigationViewPageProvider();
            var appearanceConfigurator = new FakeMainWindowAppearanceConfigurator();
            using var serviceProvider = new Microsoft.Extensions.DependencyInjection.ServiceCollection().BuildServiceProvider();

            var window = new MainWindow(
                new MainWindowViewModel(),
                navigationService,
                pageProvider,
                serviceProvider,
                appearanceConfigurator);

            window.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.FrameworkElement.LoadedEvent));
            window.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.FrameworkElement.LoadedEvent));

            Assert.True(appearanceConfigurator.ConfigureCallCount >= 1);
            Assert.Same(GetNavigationView(window), navigationService.NavigationControl);
            Assert.Equal(typeof(LibraryPage), navigationService.LastNavigationPageType);
            Assert.Equal(1, navigationService.NavigateCallCount);
        });
    }

    [Fact]
    public void Shell_exposes_only_library_and_settings_primary_items()
    {
        WpfTestHost.RunInSta(() =>
        {
            using var serviceProvider = new Microsoft.Extensions.DependencyInjection.ServiceCollection().BuildServiceProvider();
            var window = new MainWindow(
                new MainWindowViewModel(),
                new FakeNavigationService(),
                new FakeNavigationViewPageProvider(),
                serviceProvider,
                new FakeMainWindowAppearanceConfigurator());

            var navigationView = GetNavigationView(window);

            Assert.Equal(2, navigationView.MenuItems.Count);

            var firstItem = Assert.IsType<NavigationViewItem>(navigationView.MenuItems[0]);
            var secondItem = Assert.IsType<NavigationViewItem>(navigationView.MenuItems[1]);

            Assert.Equal("书库", firstItem.Content);
            Assert.Equal(typeof(LibraryPage), firstItem.TargetPageType);
            Assert.Equal("设置", secondItem.Content);
            Assert.Equal(typeof(SettingsPage), secondItem.TargetPageType);
        });
    }

    private static NavigationView GetNavigationView(MainWindow window)
    {
        var property = typeof(MainWindow).GetProperty("NavigationViewControl", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        return Assert.IsType<NavigationView>(property?.GetValue(window));
    }

    private sealed class FakeNavigationService : INavigationService
    {
        public INavigationView? NavigationControl { get; private set; }

        public Type? LastNavigationPageType { get; private set; }

        public int NavigateCallCount { get; private set; }

        public INavigationView GetNavigationControl()
        {
            return NavigationControl!;
        }

        public bool GoBack()
        {
            return false;
        }

        public bool Navigate(Type pageType)
        {
            LastNavigationPageType = pageType;
            NavigateCallCount++;
            return true;
        }

        public bool Navigate(Type pageType, object? dataContext)
        {
            LastNavigationPageType = pageType;
            NavigateCallCount++;
            return true;
        }

        public bool Navigate(string pageIdOrTargetTag) => true;

        public bool Navigate(string pageIdOrTargetTag, object? dataContext) => true;

        public bool NavigateWithHierarchy(Type pageType) => true;

        public bool NavigateWithHierarchy(Type pageType, object? dataContext) => true;

        public void SetNavigationControl(INavigationView navigation)
        {
            NavigationControl = navigation;
        }
    }

    private sealed class FakeNavigationViewPageProvider : INavigationViewPageProvider
    {
        public object GetPage(Type pageType)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FakeMainWindowAppearanceConfigurator : IMainWindowAppearanceConfigurator
    {
        public int ConfigureCallCount { get; private set; }

        public void Configure(FluentWindow window)
        {
            ConfigureCallCount++;
        }
    }
}
