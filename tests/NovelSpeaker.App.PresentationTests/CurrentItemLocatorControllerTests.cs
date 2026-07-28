using NovelSpeaker.App.Shared.Presentation.Scrolling;
using Xunit;

namespace NovelSpeaker.App.PresentationTests;

public sealed class CurrentItemLocatorControllerTests
{
    [Fact]
    public void User_scroll_shows_locator_only_after_current_item_leaves_view_and_hides_when_it_returns()
    {
        var controller = new CurrentItemLocatorController();

        controller.ObserveCurrentItem(hasCurrentItem: true, isVisible: false);
        Assert.False(controller.IsLocatorVisible);

        controller.NotifyUserScrollInput();
        controller.ObserveCurrentItem(hasCurrentItem: true, isVisible: false);
        Assert.True(controller.IsLocatorVisible);

        controller.NotifyUserScrollInput();
        controller.ObserveCurrentItem(hasCurrentItem: true, isVisible: true);
        Assert.False(controller.IsLocatorVisible);
    }

    [Fact]
    public void Current_item_change_hides_locator_until_the_user_scrolls_again()
    {
        var controller = new CurrentItemLocatorController();
        controller.NotifyUserScrollInput();
        controller.ObserveCurrentItem(hasCurrentItem: true, isVisible: false);
        Assert.True(controller.IsLocatorVisible);

        controller.NotifyCurrentItemChanged();
        controller.ObserveCurrentItem(hasCurrentItem: true, isVisible: false);
        Assert.False(controller.IsLocatorVisible);

        controller.NotifyUserScrollInput();
        controller.ObserveCurrentItem(hasCurrentItem: true, isVisible: false);
        Assert.True(controller.IsLocatorVisible);
    }

    [Fact]
    public void Locate_request_stays_visible_during_programmatic_scroll_and_hides_on_arrival()
    {
        var controller = new CurrentItemLocatorController();
        controller.NotifyUserScrollInput();
        controller.ObserveCurrentItem(hasCurrentItem: true, isVisible: false);

        Assert.True(controller.TryBeginLocate());
        controller.ObserveCurrentItem(hasCurrentItem: true, isVisible: false);
        Assert.True(controller.IsLocatorVisible);

        controller.ObserveCurrentItem(hasCurrentItem: true, isVisible: true);
        Assert.False(controller.IsLocatorVisible);
    }

    [Fact]
    public void Continuous_scroll_keeps_tracking_after_an_early_visible_update_and_can_hide_then_show_again()
    {
        var controller = new CurrentItemLocatorController();

        controller.BeginContinuousUserScroll();
        controller.ObserveCurrentItem(hasCurrentItem: true, isVisible: true);
        Assert.False(controller.IsLocatorVisible);

        controller.ObserveCurrentItem(hasCurrentItem: true, isVisible: false);
        Assert.True(controller.IsLocatorVisible);

        controller.ObserveCurrentItem(hasCurrentItem: true, isVisible: true);
        Assert.False(controller.IsLocatorVisible);

        controller.ObserveCurrentItem(hasCurrentItem: true, isVisible: false);
        Assert.True(controller.IsLocatorVisible);

        controller.ObserveCurrentItem(hasCurrentItem: true, isVisible: true);
        Assert.False(controller.IsLocatorVisible);
        controller.EndContinuousUserScroll();
    }
}
