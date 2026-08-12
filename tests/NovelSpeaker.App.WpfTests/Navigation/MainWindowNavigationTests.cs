using Microsoft.Extensions.DependencyInjection;
using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.App.Shared.Presentation.Platform;
using NovelSpeaker.App.Shared.Presentation.Controls.Settings;
using NovelSpeaker.App.Shared.Presentation.Controls.Common;
using NovelSpeaker.App.Features.Appearance;
using System.IO;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Playback.ActiveCache;
using NovelSpeaker.Application.Playback.Export;
using NovelSpeaker.App;
using NovelSpeaker.App.Shell.Navigation;
using NovelSpeaker.App.Shell.Input;
using NovelSpeaker.App.Shell.Activation;
using NovelSpeaker.App.Shell;
using NovelSpeaker.App.Shared.Theming;
using NovelSpeaker.App.Bootstrap;
using Wpf.Ui;
using Wpf.Ui.Abstractions;
using Wpf.Ui.Controls;
using Xunit;

namespace NovelSpeaker.App.WpfTests.Navigation;

[Collection("WpfDispatcher")]
public sealed class MainWindowNavigationTests
{
    [Fact]
    public async Task Active_cache_footer_entry_opens_progress_flyout_and_survives_primary_navigation()
    {
        await WpfTestHost.RunInStaAsync(async () =>
        {
            using var serviceProvider = new Microsoft.Extensions.DependencyInjection.ServiceCollection().BuildServiceProvider();
            var activeCache = new NovelSpeaker.App.WpfTests.TestDoubles.WpfFakeActiveCacheCoordinator(CreateActiveCacheSnapshot());
            var navigationService = new FakeNavigationService();
            var window = CreateWindow(
                navigationService,
                new FakeNavigationGuardService { NextResult = true },
                new FakeAppFeedbackService(),
                new FakeContentDialogService(),
                new FakeNavigationViewPageProvider(),
                new FakeSnackbarService(),
                serviceProvider,
                new FakeMainWindowAppearanceConfigurator(),
                activeCache);
            WpfWindowHost.Show(window);
            try
            {
                window.UpdateLayout();

                var entry = Assert.IsType<NavigationViewItem>(window.FindName("ActiveCacheNavigationItem"));
                Assert.Equal(Visibility.Visible, entry.Visibility);
                Assert.Equal("缓存中 · 1/3 章 · 40%", entry.Content);
                Assert.Equal("查看主动缓存进度", entry.ToolTip);
                Assert.Equal("缓存中 · 1/3 章 · 40%", AutomationProperties.GetName(entry));

                InvokeClick(entry);
                window.UpdateLayout();

                var flyout = Assert.IsType<Flyout>(window.FindName("ActiveCacheFlyout"));
                var chapterList = Assert.IsType<ListBox>(window.FindName("ActiveCacheChapterList"));
                var cancelButton = Assert.IsType<System.Windows.Controls.Button>(
                    window.FindName("CancelActiveCacheButton"));
                Assert.True(flyout.IsOpen);
                Assert.Equal(3, chapterList.Items.Count);
                Assert.Equal(
                    ["已完成", "2 / 5", "等待中"],
                    chapterList.Items.Cast<ShellActiveCacheChapterItem>().Select(item => item.StatusText));
                Assert.Equal("取消主动缓存任务", AutomationProperties.GetName(cancelButton));

                Assert.True(navigationService.Navigate(typeof(SettingsPage)));
                await DrainDispatcherAsync(window.Dispatcher);

                Assert.Equal(Visibility.Visible, entry.Visibility);
                Assert.Equal("缓存中 · 1/3 章 · 40%", entry.Content);
            }
            finally
            {
                window.Close();
                await DrainDispatcherAsync(window.Dispatcher);
            }
        });
    }

    [Fact]
    public async Task Chapter_export_footer_entry_opens_progress_flyout_and_survives_primary_navigation()
    {
        await WpfTestHost.RunInStaAsync(async () =>
        {
            using var serviceProvider = new Microsoft.Extensions.DependencyInjection.ServiceCollection().BuildServiceProvider();
            var chapterExport = new NovelSpeaker.App.WpfTests.TestDoubles.WpfFakeChapterExportCoordinator(
                new ChapterExportSnapshot(
                    Guid.NewGuid(),
                    "book-1",
                    "示例小说",
                    ChapterExportBatchStatus.Running,
                    7,
                    2,
                    0,
                    2,
                    "第三章",
                    "D:\\Exports",
                    null,
                    null));
            var navigationService = new FakeNavigationService();
            var window = CreateWindow(
                navigationService,
                new FakeNavigationGuardService { NextResult = true },
                new FakeAppFeedbackService(),
                new FakeContentDialogService(),
                new FakeNavigationViewPageProvider(),
                new FakeSnackbarService(),
                serviceProvider,
                new FakeMainWindowAppearanceConfigurator(),
                chapterExportCoordinator: chapterExport);
            WpfWindowHost.Show(window);
            try
            {
                window.UpdateLayout();

                var entry = Assert.IsType<NavigationViewItem>(window.FindName("ChapterExportNavigationItem"));
                Assert.Equal(Visibility.Visible, entry.Visibility);
                Assert.Equal("导出中 · 2/7 章 · 29%", entry.Content);
                Assert.Equal("查看章节导出进度", entry.ToolTip);

                InvokeClick(entry);
                window.UpdateLayout();

                var flyout = Assert.IsType<Flyout>(window.FindName("ChapterExportFlyout"));
                var cancelButton = Assert.IsType<System.Windows.Controls.Button>(
                    window.FindName("CancelChapterExportButton"));
                Assert.True(flyout.IsOpen);
                Assert.Equal("取消章节导出任务", AutomationProperties.GetName(cancelButton));

                Assert.True(navigationService.Navigate(typeof(SettingsPage)));
                await DrainDispatcherAsync(window.Dispatcher);

                Assert.Equal(Visibility.Visible, entry.Visibility);
                Assert.Equal("导出中 · 2/7 章 · 29%", entry.Content);
            }
            finally
            {
                window.Close();
                await DrainDispatcherAsync(window.Dispatcher);
            }
        });
    }

    [Fact]
    public async Task Closing_window_delegates_to_desktop_lifecycle_and_remains_open_when_exit_is_not_approved()
    {
        await WpfTestHost.RunInStaAsync(async () =>
        {
            var guard = new FakeNavigationGuardService { NextResult = false };
            var feedback = new FakeAppFeedbackService();
            var requestCount = 0;
            var exitApproved = false;
            var window = CreateWindow(
                guard,
                feedback,
                _ =>
                {
                    requestCount++;
                    return Task.CompletedTask;
                },
                () => exitApproved);
            WpfWindowHost.Show(window);

            window.Close();
            await DrainDispatcherAsync(window.Dispatcher);

            Assert.True(window.IsVisible);
            Assert.Equal(1, requestCount);
            Assert.Equal(0, guard.ConfirmationCount);
            Assert.Null(feedback.LastProjectedTitle);

            exitApproved = true;
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
            var window = CreateWindow(
                guard,
                new FakeAppFeedbackService(),
                _ => throw new InvalidOperationException("Exit callback must not run after approval."),
                () => true);
            WpfWindowHost.Show(window);

            window.Close();
            await DrainDispatcherAsync(window.Dispatcher);

            Assert.False(window.IsVisible);
            Assert.Equal(0, guard.ConfirmationCount);
        });
    }

    [Fact]
    public async Task Closing_guard_failure_is_projected_and_keeps_window_open()
    {
        await WpfTestHost.RunInStaAsync(async () =>
        {
            var guard = new FakeNavigationGuardService();
            var feedback = new FakeAppFeedbackService();
            var exitApproved = false;
            var window = CreateWindow(
                guard,
                feedback,
                _ => throw new InvalidOperationException("sensitive detail"),
                () => exitApproved);
            WpfWindowHost.Show(window);

            window.Close();
            await DrainDispatcherAsync(window.Dispatcher);

            Assert.True(window.IsVisible);
            Assert.Equal("关闭应用失败", feedback.LastProjectedTitle);

            exitApproved = true;
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

            var window = CreateWindow(
                navigationService,
                new FakeNavigationGuardService { NextResult = true },
                new FakeAppFeedbackService(),
                contentDialogService,
                pageProvider,
                snackbarService,
                serviceProvider,
                appearanceConfigurator);

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
            var window = CreateWindow(
                navigationService,
                new FakeNavigationGuardService { NextResult = true },
                new FakeAppFeedbackService(),
                contentDialogService,
                new FakeNavigationViewPageProvider(),
                snackbarService,
                serviceProvider,
                new FakeMainWindowAppearanceConfigurator());

            var navigationView = GetNavigationView(window);
            window.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));

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
    public async Task Main_window_uses_formal_shell_resources_without_legacy_keys_or_page_margin()
    {
        await WpfTestHost.RunInStaAsync(async () =>
        {
            var provider = WpfTestHost.BuildServiceProvider();
            var window = provider.GetRequiredService<MainWindow>();
            try
            {
                window.Width = 960;
                window.Height = 640;
                WpfWindowHost.Show(window);
                await DrainDispatcherAsync(window.Dispatcher);
                window.UpdateLayout();

                var navigationView = GetNavigationView(window);
                Assert.Equal(220, navigationView.OpenPaneLength);
                Assert.Equal(64, navigationView.CompactPaneLength);
                var navigationStyle = window.FindResource("App.Navigation.Entry");
                Assert.All(
                    navigationView.MenuItems.Cast<NavigationViewItem>(),
                    item => Assert.Same(navigationStyle, item.Style));
                Assert.All(
                    navigationView.FooterMenuItems.Cast<NavigationViewItem>(),
                    item => Assert.Same(navigationStyle, item.Style));

                var activeCacheFlyout = Assert.IsType<Flyout>(window.FindName("ActiveCacheFlyout"));
                var chapterExportFlyout = Assert.IsType<Flyout>(window.FindName("ChapterExportFlyout"));
                Assert.Same(window.FindResource("App.Feedback.FlyoutHost"), activeCacheFlyout.Style);
                Assert.Same(window.FindResource("App.Feedback.FlyoutHost"), chapterExportFlyout.Style);
                Assert.Same(
                    window.FindResource("App.Feedback.PopupSurface"),
                    Assert.IsType<Border>(window.FindName("ActiveCacheFlyoutSurface")).Style);
                Assert.Same(
                    window.FindResource("App.Feedback.PopupSurface"),
                    Assert.IsType<Border>(window.FindName("ChapterExportFlyoutSurface")).Style);
                Assert.Same(
                    window.FindResource("App.Button.Secondary"),
                    Assert.IsType<System.Windows.Controls.Button>(window.FindName("CancelActiveCacheButton")).Style);
                var dialogHost = Assert.IsType<ContentDialogHost>(window.FindName("RootContentDialogHost"));
                var snackbarPresenter = Assert.IsType<SnackbarPresenter>(window.FindName("RootSnackbarPresenter"));
                Assert.True(Panel.GetZIndex(dialogHost) > Panel.GetZIndex(snackbarPresenter));
                var snackbarStyle = Assert.IsType<Style>(snackbarPresenter.Resources[typeof(Snackbar)]);
                Assert.Same(window.FindResource("App.Feedback.Snackbar"), snackbarStyle.BasedOn);

                var presenter = Assert.IsType<NavigationViewContentPresenter>(
                    VisualTreeTestHelper.FindDescendant<NavigationViewContentPresenter>(navigationView));
                Assert.True(presenter.ActualWidth > 0);
                Assert.True(presenter.ActualHeight > 0);
                var libraryPage = Assert.IsType<LibraryPage>(
                    VisualTreeTestHelper.FindDescendant<LibraryPage>(presenter));
                var pageHeader = Assert.IsType<AppPageHeader>(libraryPage.FindName("PageHeader"));
                Assert.Equal(new Thickness(0), libraryPage.Margin);
                Assert.InRange(
                    Math.Abs(libraryPage.ActualWidth - presenter.ActualWidth),
                    0,
                    1);
                Assert.InRange(
                    Math.Abs(libraryPage.ActualHeight - presenter.ActualHeight),
                    0,
                    1);
                var headerOrigin = pageHeader.TranslatePoint(new Point(), libraryPage);
                Assert.True(headerOrigin.X >= 0);
                Assert.True(headerOrigin.Y >= 0);
                Assert.True(headerOrigin.X + pageHeader.ActualWidth <= libraryPage.ActualWidth);
                Assert.True(headerOrigin.Y + pageHeader.ActualHeight <= libraryPage.ActualHeight);

                var xaml = File.ReadAllText(Path.Combine(
                    LocateRepositoryRoot(),
                    "src",
                    "NovelSpeaker.App",
                    "Shell",
                    "MainWindow.xaml"));
                Assert.Contains("App.Brush.Window.Background", xaml, StringComparison.Ordinal);
                foreach (var legacyKey in new[]
                         {
                             "ApplicationBackgroundBrush",
                             "LayerFillColorAltBrush",
                             "NavigationPaneOpenWidth",
                             "NavigationPaneCompactWidth",
                             "CardPaddingLarge",
                             "PopupSurfaceBorderStyle",
                             "StrongTextBlockStyle",
                             "SecondaryTextBlockStyle",
                             "SectionTitleTextBlockStyle",
                             "PrimaryTextBlockStyle"
                         })
                {
                    Assert.DoesNotContain(legacyKey, xaml, StringComparison.Ordinal);
                }

                var themeRuntime = new WpfUiThemeRuntime();
                foreach (var applyTheme in new Action[] { themeRuntime.ApplyLightTheme, themeRuntime.ApplyDarkTheme })
                {
                    applyTheme();
                    window.UpdateLayout();
                    var activeNavigationItem = navigationView.MenuItems
                        .Cast<NavigationViewItem>()
                        .Single(item => item.IsActive);
                    var foreground = Assert.IsType<SolidColorBrush>(activeNavigationItem.Foreground).Color;
                    var background = Assert.IsType<SolidColorBrush>(activeNavigationItem.Background).Color;
                    Assert.True(
                        ContrastRatio(foreground, background) >= 4.5,
                        $"Active navigation contrast was {ContrastRatio(foreground, background):0.00}:1.");
                    foreach (var scale in new[] { 1d, 1.25d, 1.5d })
                    {
                        var bitmap = new RenderTargetBitmap(
                            (int)Math.Round(960 * scale),
                            (int)Math.Round(640 * scale),
                            96 * scale,
                            96 * scale,
                            PixelFormats.Pbgra32);
                        bitmap.Render(window);
                        Assert.Equal((int)Math.Round(960 * scale), bitmap.PixelWidth);
                        Assert.Equal((int)Math.Round(640 * scale), bitmap.PixelHeight);
                    }
                }
            }
            finally
            {
                new WpfUiThemeRuntime().ApplyLightTheme();
                window.Close();
                await DrainDispatcherAsync(window.Dispatcher);
                await provider.DisposeAsync();
            }
        });
    }

    [Fact]
    public void Main_window_visual_review_generates_stable_window_screenshots()
    {
        if (!VisualArtifactTestGuard.IsEnabled)
        {
            return;
        }

        WpfTestHost.RunInSta(() =>
        {
            WindowVisualReviewHarness.GenerateAndVerifyRepeatable(
                LocateRepositoryRoot(),
                "main-window",
                960,
                640,
                [
                    new WindowVisualReviewScenario("default", 1d),
                    new WindowVisualReviewScenario("default", 1.25d),
                    new WindowVisualReviewScenario("default", 1.5d)
                ],
                CreateVisualReviewWindow);
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
                WpfWindowHost.Show(window);
                window.UpdateLayout();

                var navigationService = provider.GetRequiredService<IAppNavigator>();
                Assert.True(await navigationService.NavigateAsync(new PlayerRoute("book-1"), CancellationToken.None));

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
    public async Task Real_navigation_to_appearance_settings_page_does_not_raise_dispatcher_exception()
    {
        await WpfTestHost.RunInStaAsync(async () =>
        {
            var themeRuntime = new WpfUiThemeRuntime();
            themeRuntime.ApplySystemTheme();
            var provider = WpfTestHost.BuildServiceProvider();
            var window = provider.GetRequiredService<MainWindow>();
            Exception? dispatcherException = null;
            DispatcherUnhandledExceptionEventHandler handler = (_, args) =>
            {
                dispatcherException = args.Exception;
                args.Handled = true;
            };

            window.Dispatcher.UnhandledException += handler;
            try
            {
                WpfWindowHost.Show(window);
                window.UpdateLayout();
                await DrainDispatcherAsync(window.Dispatcher);

                var navigator = provider.GetRequiredService<IAppNavigator>();
                Assert.True(await navigator.NavigateAsync(AppRoutes.Settings, CancellationToken.None));
                await DrainDispatcherAsync(window.Dispatcher);

                var settingsPage = Assert.IsType<SettingsPage>(
                    VisualTreeTestHelper.FindDescendant<SettingsPage>(GetNavigationView(window)));
                var appearanceRow = Assert.Single(
                    VisualTreeTestHelper.FindDescendants<AppSettingsNavigationRow>(settingsPage),
                    row => row.Title == "外观");
                InvokeClick(appearanceRow);
                await DrainDispatcherAsync(window.Dispatcher);

                Assert.Null(dispatcherException);
                Assert.IsType<AppearanceSettingsPage>(
                    VisualTreeTestHelper.FindDescendant<AppearanceSettingsPage>(GetNavigationView(window)));
                Assert.Equal(
                    AppRouteId.AppearanceSettings,
                    provider.GetRequiredService<IShellNavigationAdapter>().CurrentRouteId);
            }
            finally
            {
                window.Dispatcher.UnhandledException -= handler;
                window.Close();
                await DrainDispatcherAsync(window.Dispatcher);
                await provider.DisposeAsync();
                themeRuntime.ApplyLightTheme();
            }
        });
    }

    [Fact]
    public async Task Navigation_content_host_uses_semantic_canvas_border_and_rounds_its_top_left_corner()
    {
        await WpfTestHost.RunInStaAsync(async () =>
        {
            var provider = WpfTestHost.BuildServiceProvider();
            var window = provider.GetRequiredService<MainWindow>();
            try
            {
                WpfWindowHost.Show(window);
                window.UpdateLayout();

                var application = Assert.IsAssignableFrom<global::System.Windows.Application>(
                    global::System.Windows.Application.Current);
                var canvas = Assert.IsType<SolidColorBrush>(application.FindResource("App.Brush.Canvas"));
                var borderBrush = Assert.IsType<SolidColorBrush>(
                    application.FindResource("App.Brush.Border.Subtle"));
                var navigationView = GetNavigationView(window);
                var presenter = Assert.IsType<NavigationViewContentPresenter>(
                    VisualTreeTestHelper.FindDescendant<NavigationViewContentPresenter>(navigationView));

                var contentHost = Assert.IsType<Border>(FindVisualAncestor<Border>(presenter));
                Assert.Equal(canvas.Color, Assert.IsType<SolidColorBrush>(contentHost.Background).Color);
                Assert.Equal(borderBrush.Color, Assert.IsType<SolidColorBrush>(contentHost.BorderBrush).Color);
                Assert.True(contentHost.CornerRadius.TopLeft > 0);
            }
            finally
            {
                window.Close();
                await DrainDispatcherAsync(window.Dispatcher);
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
                WpfWindowHost.Show(window);
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

    private static string LocateRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "src")) &&
                Directory.Exists(Path.Combine(current.FullName, "docs")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }

    private static double ContrastRatio(Color foreground, Color background)
    {
        var foregroundLuminance = RelativeLuminance(foreground);
        var backgroundLuminance = RelativeLuminance(background);
        return (Math.Max(foregroundLuminance, backgroundLuminance) + 0.05) /
               (Math.Min(foregroundLuminance, backgroundLuminance) + 0.05);
    }

    private static double RelativeLuminance(Color color)
    {
        static double Linearize(byte component)
        {
            var channel = component / 255d;
            return channel <= 0.04045
                ? channel / 12.92
                : Math.Pow((channel + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Linearize(color.R)) +
               (0.7152 * Linearize(color.G)) +
               (0.0722 * Linearize(color.B));
    }

    private static WindowVisualReviewWindow CreateVisualReviewWindow()
    {
        var provider = WpfTestHost.BuildServiceProvider();
        var window = provider.GetRequiredService<MainWindow>();
        window.ConfigureDesktopLifecycle(_ => Task.CompletedTask, () => true);
        return new WindowVisualReviewWindow(
            window,
            () => provider.DisposeAsync().AsTask().GetAwaiter().GetResult(),
            () => provider.GetRequiredService<IAppNavigator>()
                .NavigateAsync(AppRoutes.Settings, CancellationToken.None)
                .GetAwaiter()
                .GetResult());
    }

    private static T? FindVisualAncestor<T>(DependencyObject element)
        where T : DependencyObject
    {
        for (var current = VisualTreeHelper.GetParent(element);
             current is not null;
             current = VisualTreeHelper.GetParent(current))
        {
            if (current is T match)
            {
                return match;
            }
        }

        return null;
    }

    private static MainWindow CreateWindow(
        INavigationGuardService navigationGuardService,
        IAppFeedbackService feedbackService,
        Func<CancellationToken, Task>? requestCloseAsync = null,
        Func<bool>? isExitApproved = null)
    {
        var navigationService = new FakeNavigationService();
        var serviceProvider = new Microsoft.Extensions.DependencyInjection.ServiceCollection().BuildServiceProvider();
        return CreateWindow(
            navigationService,
            navigationGuardService,
            feedbackService,
            new FakeContentDialogService(),
            new FakeNavigationViewPageProvider(),
            new FakeSnackbarService(),
            serviceProvider,
            new FakeMainWindowAppearanceConfigurator(),
            requestCloseAsync: requestCloseAsync,
            isExitApproved: isExitApproved);
    }

    private static MainWindow CreateWindow(
        FakeNavigationService navigationService,
        INavigationGuardService navigationGuardService,
        IAppFeedbackService feedbackService,
        IContentDialogService contentDialogService,
        INavigationViewPageProvider pageProvider,
        ISnackbarService snackbarService,
        IServiceProvider serviceProvider,
        IMainWindowAppearanceConfigurator appearanceConfigurator,
        IActiveCacheCoordinator? activeCacheCoordinator = null,
        IChapterExportCoordinator? chapterExportCoordinator = null,
        Func<CancellationToken, Task>? requestCloseAsync = null,
        Func<bool>? isExitApproved = null)
    {
        var layoutController = new ShellLayoutController();
        var platformAdapter = new WpfShellPlatformAdapter(
            appearanceConfigurator,
            contentDialogService,
            navigationService,
            pageProvider,
            serviceProvider,
            snackbarService);
        var activationCoordinator = new ShellActivationCoordinator(
            layoutController,
            navigationService,
            platformAdapter,
            new ProcessShutdownGate());

        var window = new MainWindow(
            new MainWindowViewModel(
                new FakePlaybackCoordinator(),
                new ShellActiveCacheController(
                    activeCacheCoordinator ?? new NovelSpeaker.App.WpfTests.TestDoubles.WpfFakeActiveCacheCoordinator(),
                    feedbackService),
                new ShellChapterExportController(
                    chapterExportCoordinator ?? new NovelSpeaker.App.WpfTests.TestDoubles.WpfFakeChapterExportCoordinator(),
                    feedbackService,
                    new FakePresentationLauncher()),
                navigationService),
            feedbackService,
            activationCoordinator,
            layoutController,
            new FakeKeyboardShortcutCoordinator(),
            new WpfShortcutContextResolver());
        window.ConfigureDesktopLifecycle(
            requestCloseAsync ?? (_ => Task.CompletedTask),
            isExitApproved ?? (() => false));
        return window;
    }

    private static ActiveCacheSnapshot CreateActiveCacheSnapshot() =>
        new(
            Guid.NewGuid(),
            "book-1",
            "示例小说",
            ActiveCacheBatchStatus.Running,
            1,
            3,
            4,
            10,
            1,
            "第二章",
            [
                new ActiveCacheChapterSnapshot(0, "第一章", 3, 3, ActiveCacheChapterStatus.Completed, null),
                new ActiveCacheChapterSnapshot(1, "第二章", 2, 5, ActiveCacheChapterStatus.Running, null),
                new ActiveCacheChapterSnapshot(2, "第三章", 0, 2, ActiveCacheChapterStatus.Pending, null)
            ],
            null);

    private static void InvokeClick(FrameworkElement item)
    {
        var onClick = item.GetType().GetMethod("OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(onClick);
        onClick!.Invoke(item, []);
    }

    private static Task DrainDispatcherAsync(Dispatcher dispatcher)
    {
        return dispatcher.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle).Task;
    }

    private sealed class FakePresentationLauncher : IPresentationLauncher
    {
        public Task OpenAsync(string path, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeNavigationService : INavigationService, IShellNavigationAdapter
    {
        public INavigationView? NavigationControl { get; private set; }

        public Type? LastNavigationPageType { get; private set; }

        public int NavigateCallCount { get; private set; }

        public bool IsBypassingGuard => false;

        public AppRouteId CurrentRouteId { get; private set; } = AppRouteId.Library;

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

        public Task<bool> NavigateAsync(AppRoute route, CancellationToken cancellationToken, bool bypassGuard = false)
        {
            CurrentRouteId = route.Id;
            LastNavigationPageType = route.Id == AppRouteId.Library ? typeof(LibraryPage) : null;
            NavigateCallCount++;
            return Task.FromResult(true);
        }

        public void Initialize(
            INavigationView navigationView,
            NavigationViewItem libraryItem,
            NavigationViewItem settingsItem,
            NavigationViewItem playbackItem)
        {
            NavigationControl = navigationView;
            libraryItem.TargetPageType = typeof(LibraryPage);
            settingsItem.TargetPageType = typeof(SettingsPage);
        }

        public Task<bool> NavigateFromShellAsync(
            NavigatingCancelEventArgs eventArgs,
            CancellationToken cancellationToken) => Task.FromResult(true);

        public void SynchronizeSelection(EventArgs eventArgs)
        {
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
