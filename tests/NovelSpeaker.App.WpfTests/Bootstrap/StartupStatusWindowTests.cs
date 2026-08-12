using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NovelSpeaker.App.Bootstrap;
using NovelSpeaker.App.Shared.Presentation.Controls.Feedback;
using NovelSpeaker.App.Shared.Theming;
using SymbolIcon = Wpf.Ui.Controls.SymbolIcon;
using Xunit;

namespace NovelSpeaker.App.WpfTests.Bootstrap;

[Collection("WpfDispatcher")]
public sealed class StartupStatusWindowTests
{
    [Fact]
    public void Loading_state_uses_formal_surface_status_and_progress_resources()
    {
        WpfTestHost.RunInSta(() =>
        {
            var viewModel = new StartupStatusViewModel();
            var window = new StartupStatusWindow(viewModel);
            WpfWindowHost.Show(window);
            try
            {
                window.UpdateLayout();

                var surface = Assert.IsType<Border>(window.FindName("StartupSurface"));
                var status = Assert.IsType<AppStatusView>(window.FindName("StartupStatusView"));
                var progress = Assert.IsType<ProgressBar>(window.FindName("StartupProgressBar"));
                Assert.Same(window.FindResource("App.Surface.DialogContent"), surface.Style);
                Assert.Same(
                    window.FindResource("App.Progress.Standard"),
                    progress.Style.BasedOn);
                Assert.Equal(AppStatusKind.Loading, status.Status);
                Assert.Equal(viewModel.StatusText, status.Title);
                Assert.Equal(viewModel.DetailText, status.Description);
                Assert.Equal(Visibility.Visible, progress.Visibility);
                Assert.True(progress.IsIndeterminate);

                AssertContained(surface, status);
                AssertContained(surface, progress);
                Assert.True(status.ActualWidth > 0);
                Assert.True(status.ActualHeight > 0);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Failure_state_uses_only_projected_copy_and_replaces_loading_progress()
    {
        WpfTestHost.RunInSta(() =>
        {
            var exception = new InvalidOperationException(
                @"C:\Users\reader\Novel\secret.txt Authorization=Bearer private-token https://tts.example/audio body=正文机密句");
            var viewModel = new StartupStatusViewModel();
            var window = new StartupStatusWindow(viewModel);
            WpfWindowHost.Show(window);
            try
            {
                var projected = StartupFailureProjector.Project(StartupStage.Database);
                viewModel.ShowFailure(projected);
                window.UpdateLayout();

                var status = Assert.IsType<AppStatusView>(window.FindName("StartupStatusView"));
                var progress = Assert.IsType<ProgressBar>(window.FindName("StartupProgressBar"));
                Assert.True(viewModel.HasError);
                Assert.Equal(AppStatusKind.Error, status.Status);
                Assert.Equal("启动未完成", status.Title);
                Assert.Equal(projected.Message, status.Description);
                Assert.NotEqual(exception.Message, status.Description);
                Assert.Equal(Visibility.Collapsed, progress.Visibility);
                Assert.DoesNotContain("C:\\Users", status.Description, StringComparison.Ordinal);
                Assert.DoesNotContain("private-token", status.Description, StringComparison.Ordinal);
                Assert.DoesNotContain("tts.example", status.Description, StringComparison.Ordinal);
                Assert.DoesNotContain("正文机密句", status.Description, StringComparison.Ordinal);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void Light_dark_and_supported_dpi_keep_core_status_inside_the_window()
    {
        WpfTestHost.RunInSta(() =>
        {
            var themeRuntime = new WpfUiThemeRuntime();
            try
            {
                foreach (var applyTheme in new Action[] { themeRuntime.ApplyLightTheme, themeRuntime.ApplyDarkTheme })
                {
                    applyTheme();
                    foreach (var scale in new[] { 1d, 1.25d, 1.5d })
                    {
                        foreach (var isError in new[] { false, true })
                        {
                            AssertLayoutAtDpi(scale, isError);
                        }
                    }
                }
            }
            finally
            {
                themeRuntime.ApplyLightTheme();
            }
        });
    }

    [Fact]
    public void Startup_status_visual_review_generates_stable_window_screenshots()
    {
        if (!VisualArtifactTestGuard.IsEnabled)
        {
            return;
        }

        WpfTestHost.RunInSta(() =>
        {
            WindowVisualReviewHarness.GenerateAndVerifyRepeatable(
                LocateRepositoryRoot(),
                "startup-status-window",
                420,
                220,
                [
                    new WindowVisualReviewScenario("loading", 1d),
                    new WindowVisualReviewScenario("loading", 1.25d),
                    new WindowVisualReviewScenario("loading", 1.5d),
                    new WindowVisualReviewScenario("error", 1d, ConfigureFailure),
                    new WindowVisualReviewScenario("error", 1.25d, ConfigureFailure),
                    new WindowVisualReviewScenario("error", 1.5d, ConfigureFailure)
                ],
                static () => new WindowVisualReviewWindow(
                    new StartupStatusWindow(new StartupStatusViewModel()),
                    static () => { }),
                useActualClientSize: true);
        });
    }

    [Fact]
    public void Startup_status_window_contains_no_legacy_visual_resource_keys()
    {
        var xaml = File.ReadAllText(Path.Combine(
            LocateRepositoryRoot(),
            "src",
            "NovelSpeaker.App",
            "Bootstrap",
            "StartupStatusWindow.xaml"));

        Assert.Contains("App.Surface.DialogContent", xaml, StringComparison.Ordinal);
        Assert.Contains("App.Progress.Standard", xaml, StringComparison.Ordinal);
        Assert.Contains("AppStatusView", xaml, StringComparison.Ordinal);
    }

    private static void ConfigureFailure(Window window)
    {
        var viewModel = Assert.IsType<StartupStatusViewModel>(window.DataContext);
        viewModel.ShowFailure(StartupFailureProjector.Project(StartupStage.Database));
    }

    private static void AssertLayoutAtDpi(double scale, bool isError)
    {
        var viewModel = new StartupStatusViewModel();
        if (isError)
        {
            viewModel.ShowFailure(StartupFailureProjector.Project(StartupStage.Database));
        }

        var window = new StartupStatusWindow(viewModel);
        WpfWindowHost.Show(window);
        try
        {
            window.UpdateLayout();
            var content = Assert.IsAssignableFrom<FrameworkElement>(window.Content);
            var clientSize = new Size(content.ActualWidth, content.ActualHeight);
            Assert.True(clientSize.Width < window.Width);
            Assert.True(clientSize.Height < window.Height);
            window.Content = null;

            var dpiRoot = new Border { Child = content, DataContext = viewModel };
            dpiRoot.SetResourceReference(Border.BackgroundProperty, "App.Brush.Window.Background");
            VisualTreeHelper.SetRootDpi(dpiRoot, new DpiScale(scale, scale));
            dpiRoot.Measure(clientSize);
            dpiRoot.Arrange(new Rect(new Point(), clientSize));
            dpiRoot.UpdateLayout();

            var actualDpi = VisualTreeHelper.GetDpi(dpiRoot);
            Assert.Equal(scale, actualDpi.DpiScaleX, 3);
            Assert.Equal(scale, actualDpi.DpiScaleY, 3);
            var surface = Assert.IsType<Border>(window.FindName("StartupSurface"));
            var status = Assert.IsType<AppStatusView>(window.FindName("StartupStatusView"));
            var progress = Assert.IsType<ProgressBar>(window.FindName("StartupProgressBar"));
            var title = Assert.IsType<TextBlock>(status.Template.FindName("TitlePresenter", status));
            var description = Assert.IsType<TextBlock>(status.Template.FindName("DescriptionPresenter", status));
            var icon = Assert.IsType<SymbolIcon>(status.Template.FindName("IconPresenter", status));

            AssertContained(dpiRoot, surface);
            AssertContained(surface, status);
            AssertContained(status, title);
            AssertContained(status, description);
            AssertContained(status, icon);
            Assert.True(title.ActualWidth > 0);
            Assert.True(description.ActualWidth > 0);
            Assert.True(icon.ActualWidth > 0);

            if (isError)
            {
                Assert.Equal(Visibility.Collapsed, progress.Visibility);
            }
            else
            {
                Assert.Equal(Visibility.Visible, progress.Visibility);
                AssertContained(surface, progress);
                var statusOrigin = status.TranslatePoint(new Point(), surface);
                var progressOrigin = progress.TranslatePoint(new Point(), surface);
                Assert.True(statusOrigin.Y + status.ActualHeight <= progressOrigin.Y);
            }

            dpiRoot.Child = null;
        }
        finally
        {
            window.Close();
        }
    }

    private static void AssertContained(FrameworkElement parent, FrameworkElement child)
    {
        var origin = child.TranslatePoint(new Point(), parent);
        Assert.True(origin.X >= 0);
        Assert.True(origin.Y >= 0);
        Assert.True(origin.X + child.ActualWidth <= parent.ActualWidth + 0.5);
        Assert.True(origin.Y + child.ActualHeight <= parent.ActualHeight + 0.5);
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
}
