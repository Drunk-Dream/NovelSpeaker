using Microsoft.Extensions.DependencyInjection;
using System.Windows.Controls;
using System.Windows;
using System.Windows.Media;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.App;
using NovelSpeaker.App.Navigation;
using NovelSpeaker.App.Pages;
using NovelSpeaker.App.Shell;
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
            var contentDialogService = new FakeContentDialogService();
            var snackbarService = new FakeSnackbarService();
            using var serviceProvider = new Microsoft.Extensions.DependencyInjection.ServiceCollection().BuildServiceProvider();

            var window = new MainWindow(
                new MainWindowViewModel(new FakePlaybackCoordinator(), navigationService),
                contentDialogService,
                navigationService,
                pageProvider,
                snackbarService,
                serviceProvider,
                appearanceConfigurator,
                new ShellLayoutController());

            window.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.FrameworkElement.LoadedEvent));
            window.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.FrameworkElement.LoadedEvent));

            Assert.Equal(1, appearanceConfigurator.ConfigureCallCount);
            Assert.Equal(1, contentDialogService.SetDialogHostCallCount);
            Assert.Equal(1, snackbarService.SetPresenterCallCount);
            Assert.Same(GetNavigationView(window), navigationService.NavigationControl);
            Assert.Equal(typeof(LibraryPage), navigationService.LastNavigationPageType);
            Assert.Equal(1, navigationService.NavigateCallCount);

            var presenter = FindDescendant<NavigationViewContentPresenter>(GetNavigationView(window));
            Assert.NotNull(presenter);
            Assert.False(presenter!.IsDynamicScrollViewerEnabled);
        });
    }

    [Fact]
    public void Shell_exposes_only_library_and_settings_primary_items()
    {
        WpfTestHost.RunInSta(() =>
        {
            using var serviceProvider = new Microsoft.Extensions.DependencyInjection.ServiceCollection().BuildServiceProvider();
            var contentDialogService = new FakeContentDialogService();
            var snackbarService = new FakeSnackbarService();
            var window = new MainWindow(
                new MainWindowViewModel(new FakePlaybackCoordinator(), new FakeNavigationService()),
                contentDialogService,
                new FakeNavigationService(),
                new FakeNavigationViewPageProvider(),
                snackbarService,
                serviceProvider,
                new FakeMainWindowAppearanceConfigurator(),
                new ShellLayoutController());

            var navigationView = GetNavigationView(window);

            Assert.Equal(2, navigationView.MenuItems.Count);

            var firstItem = Assert.IsType<NavigationViewItem>(navigationView.MenuItems[0]);
            var secondItem = Assert.IsType<NavigationViewItem>(navigationView.MenuItems[1]);

            Assert.Equal("书库", firstItem.Content);
            Assert.Equal(typeof(LibraryPage), firstItem.TargetPageType);
            Assert.Equal("设置", secondItem.Content);
            Assert.Equal(typeof(SettingsPage), secondItem.TargetPageType);
            Assert.True(navigationView.IsPaneToggleVisible);
            Assert.Equal(1280d, window.Width);
            Assert.Equal(820d, window.Height);
            Assert.Equal(900d, window.MinWidth);
            Assert.Equal(640d, window.MinHeight);
            Assert.True(window.ExtendsContentIntoTitleBar);
        });
    }

    [Fact]
    public void Real_shell_navigation_to_player_page_keeps_navigation_content_presenter_configuration()
    {
        WpfTestHost.RunInSta(() =>
        {
            var provider = WpfTestHost.BuildServiceProvider();
            var window = provider.GetRequiredService<MainWindow>();
            try
            {
                window.Show();
                window.UpdateLayout();

                var navigationService = provider.GetRequiredService<INavigationService>();
                Assert.True(navigationService.Navigate(typeof(PlayerPage)));

                window.UpdateLayout();

                var navigationView = GetNavigationView(window);
                var presenter = FindDescendant<NavigationViewContentPresenter>(navigationView);
                Assert.NotNull(presenter);
                Assert.False(presenter!.IsDynamicScrollViewerEnabled);
            }
            finally
            {
                window.Close();
                provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
    }

    private static NavigationView GetNavigationView(MainWindow window)
    {
        var property = typeof(MainWindow).GetProperty("NavigationViewControl", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        return Assert.IsType<NavigationView>(property?.GetValue(window));
    }

    private static T? FindDescendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var childIndex = 0; childIndex < VisualTreeHelper.GetChildrenCount(root); childIndex++)
        {
            var child = VisualTreeHelper.GetChild(root, childIndex);
            if (child is T typedChild)
            {
                return typedChild;
            }

            var descendant = FindDescendant<T>(child);
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
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

        public void Configure(Window window)
        {
            ConfigureCallCount++;
        }
    }

    private sealed class FakeContentDialogService : IContentDialogService
    {
        public int SetDialogHostCallCount { get; private set; }

        public void SetDialogHost(ContentPresenter contentPresenter)
        {
            SetDialogHostCallCount++;
        }

        public void SetContentPresenter(ContentPresenter contentPresenter)
        {
        }

        public void SetDialogHost(ContentDialogHost contentDialogHost)
        {
            SetDialogHostCallCount++;
        }

        public ContentPresenter GetDialogHost() => new();

        public ContentPresenter GetContentPresenter() => new();

        public ContentDialogHost GetDialogHostEx() => new();

        public Task<ContentDialogResult> ShowAsync(ContentDialog dialog, CancellationToken cancellationToken)
        {
            return Task.FromResult(ContentDialogResult.None);
        }
    }

    private sealed class FakeSnackbarService : ISnackbarService
    {
        public int SetPresenterCallCount { get; private set; }

        public TimeSpan DefaultTimeOut { get; set; }

        public void SetSnackbarPresenter(SnackbarPresenter contentPresenter)
        {
            SetPresenterCallCount++;
        }

        public SnackbarPresenter GetSnackbarPresenter() => new();

        public void Show(string title, string message, ControlAppearance appearance, IconElement? icon, TimeSpan timeout)
        {
        }
    }

    private sealed class FakePlaybackCoordinator : IPlaybackCoordinator
    {
        public PlaybackSnapshot CurrentSnapshot { get; } = PlaybackSnapshot.Idle;

        public event EventHandler<PlaybackSnapshot>? SnapshotChanged
        {
            add
            {
            }
            remove
            {
            }
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public Task StartAsync(PlaybackStartRequest request, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task OpenPausedAsync(OpenBookPlaybackRequest request, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task PauseAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ResumeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task JumpToAsync(PlaybackJumpTarget target, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task JumpToChapterAsync(int chapterIndex, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task JumpToSegmentAsync(int chapterIndex, int segmentIndex, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task NextSegmentAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task PreviousSegmentAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task NextChapterAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task PreviousChapterAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task RetryCurrentSegmentAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SkipCurrentSegmentAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ChangeRuleAsync(long ruleId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ChangeSpeedAsync(int speakSpeed, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task RefreshBookMetadataAsync(string bookId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task HandleBookDeletedAsync(string bookId, CancellationToken cancellationToken) => Task.CompletedTask;
    }

}
