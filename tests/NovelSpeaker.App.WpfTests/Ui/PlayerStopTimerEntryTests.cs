using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using NovelSpeaker.App.Features.Playback.Components;
using Xunit;

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
            var popup = Assert.IsType<Popup>(view.FindName("StopTimerPopup"));
            var customMinutes = Assert.IsType<TextBox>(view.FindName("CustomStopMinutesTextBox"));

            Assert.Equal("定时停止", button.ToolTip);
            Assert.Equal("定时停止", AutomationProperties.GetName(button));
            Assert.Same(button, popup.PlacementTarget);
            Assert.Equal("自定义定时停止分钟数", AutomationProperties.GetName(customMinutes));
        });
    }
}
