using System.Windows.Automation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using NovelSpeaker.App.Desktop.MiniPlayer;
using Xunit;
using SymbolIcon = Wpf.Ui.Controls.SymbolIcon;

namespace NovelSpeaker.App.WpfTests.Desktop;

[Collection("WpfDispatcher")]
public sealed class MiniPlayerWindowTests
{
    [Fact]
    public void Window_exposes_required_controls_and_accessibility_contract()
    {
        WpfTestHost.RunInSta(() =>
        {
            var provider = WpfTestHost.BuildServiceProvider();
            try
            {
                var window = provider.GetRequiredService<MiniPlayerWindow>();
                Assert.Equal(WindowStyle.None, window.WindowStyle);
                Assert.True(window.AllowsTransparency);
                Assert.Equal(Brushes.Transparent, window.Background);

                var bookTitle = Assert.IsType<TextBlock>(window.FindName("MiniPlayerBookTitle"));
                Assert.NotNull(bookTitle.GetBindingExpression(TextBlock.TextProperty));
                var chapterTitle = Assert.IsType<TextBlock>(window.FindName("MiniPlayerChapterTitle"));
                Assert.NotNull(chapterTitle.GetBindingExpression(TextBlock.TextProperty));
                AssertControl<Button>(window, "MiniPlayerPreviousChapterButton", "上一章");
                AssertControl<Button>(window, "MiniPlayerPreviousSegmentButton", "上一段");
                AssertControl<Button>(window, "MiniPlayerPlaybackButton", "播放");
                var playbackIcon = Assert.IsType<SymbolIcon>(
                    Assert.IsType<Button>(window.FindName("MiniPlayerPlaybackButton")).Content);
                var playbackTrigger = Assert.Single(playbackIcon.Style!.Triggers.OfType<DataTrigger>());
                var playbackBinding = Assert.IsType<Binding>(playbackTrigger.Binding);
                Assert.Equal("PlaybackActionText", playbackBinding.Path.Path);
                AssertControl<Button>(window, "MiniPlayerNextSegmentButton", "下一段");
                AssertControl<Button>(window, "MiniPlayerNextChapterButton", "下一章");
                AssertControl<Button>(window, "MiniPlayerRestoreButton", "恢复主窗口");
                AssertControl<Button>(window, "MiniPlayerTopmostButton", "置顶");
                var topmostStateBorder = Assert.IsType<Border>(window.FindName("MiniPlayerTopmostStateBorder"));
                Assert.Equal(Brushes.Transparent, topmostStateBorder.Background);
                var topmostTrigger = Assert.Single(topmostStateBorder.Style!.Triggers.OfType<DataTrigger>());
                var topmostBinding = Assert.IsType<Binding>(topmostTrigger.Binding);
                Assert.Equal("IsTopmost", topmostBinding.Path.Path);
                Assert.Equal(
                    "播放进度",
                    AutomationProperties.GetName(
                        Assert.IsType<Slider>(window.FindName("MiniPlayerProgressSlider"))));
                var progressSlider = Assert.IsType<Slider>(window.FindName("MiniPlayerProgressSlider"));
                Assert.True(progressSlider.IsHitTestVisible);
                var progressToolTip = Assert.IsType<ToolTip>(progressSlider.ToolTip);
                Assert.True(progressToolTip.StaysOpen);
                Assert.False(ToolTipService.GetIsEnabled(progressSlider));
                Assert.Same(window.FindResource("PlaybackProgressSliderStyle"), progressSlider.Style);
            }
            finally
            {
                provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
    }

    [Fact]
    public void Drag_policy_allows_blank_surface_but_excludes_interactive_controls()
    {
        WpfTestHost.RunInSta(() =>
        {
            var blankSurface = new Border();
            var button = new Button();
            var slider = new Slider();
            var textBox = new TextBox();

            Assert.True(MiniPlayerWindowDragPolicy.CanStartDrag(blankSurface));
            Assert.False(MiniPlayerWindowDragPolicy.CanStartDrag(button));
            Assert.False(MiniPlayerWindowDragPolicy.CanStartDrag(slider));
            Assert.False(MiniPlayerWindowDragPolicy.CanStartDrag(textBox));
        });
    }

    [Theory]
    [InlineData(double.NaN, 20)]
    [InlineData(10, double.PositiveInfinity)]
    [InlineData(-1, 20)]
    [InlineData(900, 20)]
    public void Invalid_or_offscreen_placement_uses_safe_fallback(double left, double top)
    {
        Assert.False(MiniPlayerPlacementValidator.TryValidate(
            left,
            top,
            200,
            100,
            [new MiniPlayerScreenBounds(0, 0, 1000, 800)],
            out _));
    }

    [Fact]
    public void Valid_placement_is_preserved()
    {
        Assert.True(MiniPlayerPlacementValidator.TryValidate(
            100,
            120,
            200,
            100,
            [new MiniPlayerScreenBounds(0, 0, 1000, 800)],
            out var placement));
        Assert.Equal(new MiniPlayerPlacement(100, 120), placement);
    }

    [Fact]
    public void User_close_requests_main_window_restore_instead_of_exiting()
    {
        WpfTestHost.RunInSta(() =>
        {
            var provider = WpfTestHost.BuildServiceProvider();
            try
            {
                var window = provider.GetRequiredService<MiniPlayerWindow>();
                var restoreRequested = false;
                window.RestoreRequested += (_, _) => restoreRequested = true;
                window.Show();

                window.Close();

                Assert.True(restoreRequested);
                Assert.True(window.IsVisible);
                window.CloseForShutdown();
            }
            finally
            {
                provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
    }

    [Fact]
    public void Placement_in_gap_between_monitors_is_rejected()
    {
        Assert.False(MiniPlayerPlacementValidator.TryValidate(
            700,
            100,
            200,
            100,
            [
                new MiniPlayerScreenBounds(0, 0, 600, 800),
                new MiniPlayerScreenBounds(1000, 0, 600, 800)
            ],
            out _));
    }

    private static void AssertControl<T>(MiniPlayerWindow window, string name, string automationName)
        where T : FrameworkElement
    {
        var control = Assert.IsType<T>(window.FindName(name));
        Assert.Equal(automationName, AutomationProperties.GetName(control));
        Assert.Equal(automationName, control.ToolTip);
    }
}
