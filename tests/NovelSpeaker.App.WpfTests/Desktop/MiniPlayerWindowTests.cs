using System.Windows.Automation;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using NovelSpeaker.App.Desktop.MiniPlayer;
using Xunit;

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

                var bookTitle = Assert.IsType<TextBlock>(window.FindName("MiniPlayerBookTitle"));
                Assert.NotNull(bookTitle.GetBindingExpression(TextBlock.TextProperty));
                var chapterTitle = Assert.IsType<TextBlock>(window.FindName("MiniPlayerChapterTitle"));
                Assert.NotNull(chapterTitle.GetBindingExpression(TextBlock.TextProperty));
                AssertControl<Button>(window, "MiniPlayerPreviousChapterButton", "上一章");
                AssertControl<Button>(window, "MiniPlayerPreviousSegmentButton", "上一段");
                AssertControl<Button>(window, "MiniPlayerPlaybackButton", "播放");
                AssertControl<Button>(window, "MiniPlayerNextSegmentButton", "下一段");
                AssertControl<Button>(window, "MiniPlayerNextChapterButton", "下一章");
                AssertControl<Button>(window, "MiniPlayerRestoreButton", "恢复主窗口");
                AssertControl<Button>(window, "MiniPlayerTopmostButton", "置顶");
                Assert.Equal(
                    "播放进度",
                    AutomationProperties.GetName(
                        Assert.IsType<Slider>(window.FindName("MiniPlayerProgressSlider"))));
            }
            finally
            {
                provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
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
