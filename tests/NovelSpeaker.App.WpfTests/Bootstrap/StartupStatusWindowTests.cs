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
    public void Startup_status_state_contracts_cover_loading_failure_and_projected_copy()
    {
        Loading_state_uses_single_surface_embedded_status_and_progress_resources();
        Failure_state_uses_only_projected_copy_and_replaces_loading_progress();
    }

    private void Loading_state_uses_single_surface_embedded_status_and_progress_resources()
    {
        WpfTestHost.RunInSta(() =>
        {
            var viewModel = new StartupStatusViewModel();
            var window = new StartupStatusWindow(viewModel);
            WpfWindowHost.Show(window);
            try
            {
                window.UpdateLayout();

                var body = Assert.IsType<Border>(window.FindName("StartupBody"));
                var status = Assert.IsType<AppStatusView>(window.FindName("StartupStatusView"));
                var progress = Assert.IsType<ProgressBar>(window.FindName("StartupProgressBar"));
                Assert.Equal(new Thickness(0), body.BorderThickness);
                Assert.Null(body.Effect);
                Assert.True(status.IsEmbedded);
                Assert.Same(
                    window.FindResource("App.Progress.Standard"),
                    progress.Style.BasedOn);
                Assert.Equal(AppStatusKind.Loading, status.Status);
                Assert.Equal(viewModel.StatusText, status.Title);
                Assert.Equal(viewModel.DetailText, status.Description);
                Assert.Equal(Visibility.Visible, progress.Visibility);
                Assert.True(progress.IsIndeterminate);

                AssertContained(body, status);
                AssertContained(body, progress);
                Assert.True(status.ActualWidth > 0);
                Assert.True(status.ActualHeight > 0);
                var statusSurface = Assert.IsType<Border>(status.Template.FindName("Surface", status));
                Assert.Equal(new Thickness(0), statusSurface.BorderThickness);
                Assert.Equal(new Thickness(0), statusSurface.Padding);
                Assert.Null(statusSurface.Effect);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private void Failure_state_uses_only_projected_copy_and_replaces_loading_progress()
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

    private static void AssertContained(FrameworkElement parent, FrameworkElement child)
    {
        var origin = child.TranslatePoint(new Point(), parent);
        Assert.True(origin.X >= 0);
        Assert.True(origin.Y >= 0);
        Assert.True(origin.X + child.ActualWidth <= parent.ActualWidth + 0.5);
        Assert.True(origin.Y + child.ActualHeight <= parent.ActualHeight + 0.5);
    }

}
