using NovelSpeaker.App.Shell;
using Xunit;

namespace NovelSpeaker.App.PresentationTests;

public sealed class ShellLayoutControllerTests
{
    [Fact]
    public void Width_below_breakpoint_collapses_pane()
    {
        var controller = new ShellLayoutController();

        controller.UpdateWindowWidth(1079);

        Assert.False(controller.IsPaneOpen);
    }

    [Fact]
    public void Manual_choice_is_restored_after_returning_to_wide_layout()
    {
        var controller = new ShellLayoutController();
        controller.UpdateWindowWidth(1400);
        controller.HandlePaneStateChanged(false);

        controller.UpdateWindowWidth(1000);
        Assert.False(controller.IsPaneOpen);

        controller.UpdateWindowWidth(1400);
        Assert.False(controller.IsPaneOpen);
    }

    [Fact]
    public void Wide_layout_tracks_manual_reopen_preference()
    {
        var controller = new ShellLayoutController();
        controller.UpdateWindowWidth(1400);
        controller.HandlePaneStateChanged(false);
        controller.HandlePaneStateChanged(true);

        Assert.True(controller.IsPaneOpen);
    }
}
