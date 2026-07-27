using System.Windows.Automation;
using System.Windows.Controls;
using NovelSpeaker.App.Features.Playback.Components;
using Xunit;

namespace NovelSpeaker.App.WpfTests.Ui;

[Collection("WpfDispatcher")]
public sealed class PlayerMiniPlayerEntryTests
{
    [Fact]
    public void Player_toolbar_exposes_mini_player_entry()
    {
        WpfTestHost.RunInSta(() =>
        {
            var view = new PlayerView();
            var button = Assert.IsType<Button>(view.FindName("MiniPlayerToolButton"));

            Assert.Equal("迷你播放器", button.ToolTip);
            Assert.Equal("迷你播放器", AutomationProperties.GetName(button));
        });
    }
}
