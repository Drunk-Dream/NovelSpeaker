using NovelSpeaker.App.Player;
using Xunit;

namespace NovelSpeaker.UnitTests.Player;

public sealed class PlayerLayoutControllerTests
{
    [Fact]
    public void Width_below_breakpoint_switches_to_compact_layout()
    {
        var controller = new PlayerLayoutController();

        controller.UpdateWidth(1000);

        Assert.True(controller.IsCompactLayout);
    }

    [Fact]
    public void Drawer_is_closed_when_returning_to_wide_layout()
    {
        var controller = new PlayerLayoutController();
        controller.UpdateWidth(1000);
        controller.OpenDrawer();

        controller.UpdateWidth(1400);

        Assert.False(controller.IsCompactLayout);
        Assert.False(controller.IsDrawerOpen);
    }

    [Fact]
    public void ToggleDrawer_only_works_in_compact_layout()
    {
        var controller = new PlayerLayoutController();

        controller.ToggleDrawer();
        Assert.False(controller.IsDrawerOpen);

        controller.UpdateWidth(1000);
        controller.ToggleDrawer();

        Assert.True(controller.IsDrawerOpen);
    }
}
