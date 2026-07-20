using Microsoft.Extensions.DependencyInjection;
using NovelSpeaker.App.Feedback;
using System.Windows.Controls;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.App;
using NovelSpeaker.App.Navigation;
using NovelSpeaker.App.Input;
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
    public async Task Closing_window_uses_guard_and_keeps_window_open_when_navigation_is_cancelled()
    {
        await WpfTestHost.RunInStaAsync(async () =>
        {
            var guard = new FakeNavigationGuardService { NextResult = false };
            var feedback = new FakeAppFeedbackService();
            var window = CreateWindow(guard, feedback);
            window.Show();

            window.Close();
            await DrainDispatcherAsync(window.Dispatcher);

            Assert.True(window.IsVisible);
            Assert.Equal(1, guard.ConfirmationCount);
            Assert.Null(feedback.LastProjectedTitle);

            guard.NextResult = true;
            window.Close();
            await DrainDispatcherAsync(window.Dispatcher);
        });
    }

    [Fact]
    public async Task Closing_window_closes_after_guard_approval()
    {
        await WpfTestHost.RunInStaAsync(async () =>
        {
            var guard = new FakeNavigationGuardService { NextResult = true };
            var window = CreateWindow(guard, new FakeAppFeedbackService());
            window.Show();

            window.Close();
            await DrainDispatcherAsync(window.Dispatcher);

            Assert.False(window.IsVisible);
            Assert.Equal(1, guard.ConfirmationCount);
        });
    }

    [Fact]
    public async Task Repeated_close_requests_share_one_pending_confirmation()
    {
        await WpfTestHost.RunInStaAsync(async () =>
        {
            var confirmation = new TaskCompletionSource<bool>();
            var guard = new FakeNavigationGuardService { PendingConfirmation = confirmation.Task };
            var window = CreateWindow(guard, new FakeAppFeedbackService());
            window.Show();

            window.Close();
            window.Close();

            Assert.True(window.IsVisible);
            Assert.Equal(1, guard.ConfirmationCount);

            confirmation.SetResult(false);
            await DrainDispatcherAsync(window.Dispatcher);

            guard.PendingConfirmation = null;
            guard.NextResult = true;
            window.Close();
            await DrainDispatcherAsync(window.Dispatcher);
        });
    }

    [Fact]
    public async Task Closing_guard_failure_is_projected_and_keeps_window_open()
    {
        await WpfTestHost.RunInStaAsync(async () =>
        {
            var guard = new FakeNavigationGuardService { Exception = new InvalidOperationException("sensitive detail") };
            var feedback = new FakeAppFeedbackService();
            var window = CreateWindow(guard, feedback);
            window.Show();

            window.Close();
            await DrainDispatcherAsync(window.Dispatcher);

            Assert.True(window.IsVisible);
            Assert.Equal("关闭应用失败", feedback.LastProjectedTitle);

            guard.Exception = null;
            guard.NextResult = true;
            window.Close();
            await DrainDispatcherAsync(window.Dispatcher);
        });
    }

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
                new FakeAppFeedbackService(),
                new FakeNavigationGuardService { NextResult = true },
                navigationService,
                navigationService,
                pageProvider,
                snackbarService,
                serviceProvider,
                appearanceConfigurator,
                new ShellLayoutController(),
                new FakeKeyboardShortcutCoordinator());

            window.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.FrameworkElement.LoadedEvent));
            window.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.FrameworkElement.LoadedEvent));

            Assert.Equal(1, appearanceConfigurator.ConfigureCallCount);
            Assert.Equal(1, contentDialogService.SetDialogHostCallCount);
            Assert.Equal(1, snackbarService.SetPresenterCallCount);
            Assert.Same(GetNavigationView(window), navigationService.NavigationControl);
            Assert.Equal(typeof(LibraryPage), navigationService.LastNavigationPageType);
            Assert.Equal(1, navigationService.NavigateCallCount);

            var presenter = VisualTreeTestHelper.FindDescendant<NavigationViewContentPresenter>(GetNavigationView(window));
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
            var navigationService = new FakeNavigationService();
            var window = new MainWindow(
                new MainWindowViewModel(new FakePlaybackCoordinator(), navigationService),
                contentDialogService,
                new FakeAppFeedbackService(),
                new FakeNavigationGuardService { NextResult = true },
                navigationService,
                navigationService,
                new FakeNavigationViewPageProvider(),
                snackbarService,
                serviceProvider,
                new FakeMainWindowAppearanceConfigurator(),
                new ShellLayoutController(),
                new FakeKeyboardShortcutCoordinator());

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
    public async Task Real_guarded_navigation_to_player_page_keeps_navigation_content_presenter_configuration()
    {
        await WpfTestHost.RunInStaAsync(async () =>
        {
            var provider = WpfTestHost.BuildServiceProvider();
            var window = provider.GetRequiredService<MainWindow>();
            try
            {
                window.Show();
                window.UpdateLayout();

                var navigationService = provider.GetRequiredService<IGuardedNavigationService>();
                Assert.True(await navigationService.NavigateWithHierarchyAsync(typeof(PlayerPage), null, CancellationToken.None));

                window.UpdateLayout();

                var navigationView = GetNavigationView(window);
                var presenter = VisualTreeTestHelper.FindDescendant<NavigationViewContentPresenter>(navigationView);
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

    [Fact]
    public async Task Primary_navigation_switch_keeps_only_one_active_menu_item()
    {
        await WpfTestHost.RunInStaAsync(async () =>
        {
            var provider = WpfTestHost.BuildServiceProvider();
            var window = provider.GetRequiredService<MainWindow>();
            try
            {
                window.Show();
                window.UpdateLayout();
                await DrainDispatcherAsync(window.Dispatcher);

                var navigationView = GetNavigationView(window);
                var libraryItem = Assert.IsType<NavigationViewItem>(navigationView.MenuItems[0]);
                var settingsItem = Assert.IsType<NavigationViewItem>(navigationView.MenuItems[1]);

                Assert.True(libraryItem.IsActive);
                Assert.False(settingsItem.IsActive);

                InvokeClick(settingsItem);
                await DrainDispatcherAsync(window.Dispatcher);

                Assert.False(libraryItem.IsActive);
                Assert.True(settingsItem.IsActive);
                Assert.Same(settingsItem, navigationView.SelectedItem);

                InvokeClick(libraryItem);
                await DrainDispatcherAsync(window.Dispatcher);

                Assert.True(libraryItem.IsActive);
                Assert.False(settingsItem.IsActive);
                Assert.Same(libraryItem, navigationView.SelectedItem);
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

    private static MainWindow CreateWindow(
        INavigationGuardService navigationGuardService,
        IAppFeedbackService feedbackService)
    {
        var navigationService = new FakeNavigationService();
        var serviceProvider = new Microsoft.Extensions.DependencyInjection.ServiceCollection().BuildServiceProvider();
        return new MainWindow(
            new MainWindowViewModel(new FakePlaybackCoordinator(), navigationService),
            new FakeContentDialogService(),
            feedbackService,
            navigationGuardService,
            navigationService,
            navigationService,
            new FakeNavigationViewPageProvider(),
            new FakeSnackbarService(),
            serviceProvider,
            new FakeMainWindowAppearanceConfigurator(),
            new ShellLayoutController(),
            new FakeKeyboardShortcutCoordinator());
    }

    private static void InvokeClick(NavigationViewItem item)
    {
        var onClick = typeof(NavigationViewItem).GetMethod("OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(onClick);
        onClick!.Invoke(item, []);
    }

    private static Task DrainDispatcherAsync(Dispatcher dispatcher)
    {
        return dispatcher.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle).Task;
    }

    private sealed class FakeNavigationService : INavigationService, IGuardedNavigationService
    {
        public INavigationView? NavigationControl { get; private set; }

        public Type? LastNavigationPageType { get; private set; }

        public int NavigateCallCount { get; private set; }

        public bool IsBypassingGuard => false;

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

        public Task<bool> GoBackAsync(CancellationToken cancellationToken, bool bypassGuard = false)
        {
            return Task.FromResult(false);
        }

        public Task<bool> NavigateAsync(string pageIdOrTargetTag, CancellationToken cancellationToken, bool bypassGuard = false)
        {
            return Task.FromResult(true);
        }

        public Task<bool> NavigateWithHierarchyAsync(Type pageType, object? dataContext, CancellationToken cancellationToken, bool bypassGuard = false)
        {
            LastNavigationPageType = pageType;
            NavigateCallCount++;
            return Task.FromResult(true);
        }
    }

    private sealed class FakeNavigationGuardService : INavigationGuardService
    {
        public bool NextResult { get; set; }

        public Task<bool>? PendingConfirmation { get; set; }

        public Exception? Exception { get; set; }

        public int ConfirmationCount { get; private set; }

        public IDisposable Register(Func<CancellationToken, Task<bool>> guard) => throw new NotSupportedException();

        public Task<bool> ConfirmNavigationAsync(CancellationToken cancellationToken)
        {
            ConfirmationCount++;
            if (Exception is not null)
            {
                throw Exception;
            }

            return PendingConfirmation ?? Task.FromResult(NextResult);
        }
    }

    private sealed class FakeAppFeedbackService : IAppFeedbackService
    {
        public string? LastProjectedTitle { get; private set; }

        public ProjectedUiError Project(Exception exception) => new("操作失败。", UiMessageSeverity.Error, false);

        public void ShowProjectedNotification(string title, ProjectedUiError projected)
        {
            LastProjectedTitle = title;
        }

        public void ShowSuccess(string title, string message) { }

        public void ShowWarning(string title, string message) { }

        public Task<AppConfirmationDecision> ConfirmDeletionAsync(
            string title,
            string message,
            CancellationToken cancellationToken) => Task.FromResult(AppConfirmationDecision.Cancel);
    }

    private sealed class FakeNavigationViewPageProvider : INavigationViewPageProvider
    {
        public object GetPage(Type pageType)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FakeKeyboardShortcutCoordinator : IKeyboardShortcutCoordinator
    {
        public Task<bool> TryHandleAsync(Key key, ModifierKeys modifiers, KeyboardShortcutContext context, CancellationToken cancellationToken)
        {
            return Task.FromResult(false);
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

    private sealed class FakePlaybackCoordinator : IPlaybackSnapshotSource
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


        public Task ChangeRuleAsync(long ruleId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ChangeSpeedAsync(int speakSpeed, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task RefreshBookMetadataAsync(string bookId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task RefreshRegexReplacementAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task HandleBookDeletedAsync(string bookId, CancellationToken cancellationToken) => Task.CompletedTask;
    }

}
