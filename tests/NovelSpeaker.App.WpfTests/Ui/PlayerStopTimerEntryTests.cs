using System.Windows.Automation;
using System.Windows.Controls;
using NovelSpeaker.App.Shared.Presentation.Behaviors;
using NovelSpeaker.App.Features.Playback.Components;
using Xunit;
using Flyout = Wpf.Ui.Controls.Flyout;

namespace NovelSpeaker.App.WpfTests.Ui;

[Collection("WpfDispatcher")]
public sealed class PlayerStopTimerEntryTests
{
    [Fact]
    public void Player_toolbar_exposes_accessible_stop_timer_flyout()
    {
        WpfTestHost.RunInSta(() =>
        {
            var view = new PlayerView();
            var button = Assert.IsType<Button>(view.FindName("StopTimerToolButton"));
            var flyout = Assert.IsType<Flyout>(view.FindName("StopTimerFlyout"));
            var customMinutes = Assert.IsType<TextBox>(view.FindName("CustomStopMinutesTextBox"));
            var cancelButton = Assert.IsType<Button>(view.FindName("CancelStopTimerButton"));
            var applyButton = Assert.IsType<Button>(view.FindName("ApplyCustomStopTimerButton"));

            Assert.Equal("定时停止", button.ToolTip);
            Assert.Equal("定时停止", AutomationProperties.GetName(button));
            Assert.Same(button, WpfUiFlyoutPlacement.GetPlacementTarget(flyout));
            Assert.Same(view.FindResource("App.Feedback.FlyoutHost"), flyout.Style);
            Assert.Equal("自定义定时停止分钟数", AutomationProperties.GetName(customMinutes));
            Assert.Equal("取消", cancelButton.Content);
            Assert.Equal("应用", applyButton.Content);
            Assert.Equal(1, Grid.GetColumn(cancelButton));
            Assert.Equal(2, Grid.GetColumn(applyButton));
            Assert.Null(VisualTreeTestHelper.FindDescendant<Button>(flyout, button =>
                button.Content is string content &&
                content.Contains("当前段", StringComparison.Ordinal)));
            Assert.Null(VisualTreeTestHelper.FindDescendant<Button>(flyout, button =>
                button.Content is string content &&
                content.Contains("当前章节", StringComparison.Ordinal)));
        });
    }
}
