using NovelSpeaker.App.Player;
using NovelSpeaker.UnitTests.Common;
using Xunit;

namespace NovelSpeaker.UnitTests.Player;

public sealed class PlayerAutoScrollCoordinatorTests
{
    [Fact]
    public void User_scroll_input_enters_manual_browsing_and_restores_after_delay()
    {
        var timeProvider = new ManualTimeProvider();
        var coordinator = new PlayerAutoScrollCoordinator(timeProvider);

        coordinator.NotifyUserScrollInput();

        Assert.Equal(PlayerAutoScrollState.ManualBrowsing, coordinator.State);
        Assert.False(coordinator.ShouldAutoCenter);
        Assert.True(coordinator.ShowReturnToCurrentSegment);

        timeProvider.Advance(TimeSpan.FromSeconds(4));

        Assert.Equal(PlayerAutoScrollState.AutoCentering, coordinator.State);
        Assert.True(coordinator.ShouldAutoCenter);
        Assert.False(coordinator.ShowReturnToCurrentSegment);
    }

    [Fact]
    public void New_scroll_input_invalidates_previous_restore_timer()
    {
        var timeProvider = new ManualTimeProvider();
        var coordinator = new PlayerAutoScrollCoordinator(timeProvider);

        coordinator.NotifyUserScrollInput();
        var firstVersion = coordinator.PendingRestoreVersion;
        timeProvider.Advance(TimeSpan.FromSeconds(2));

        coordinator.NotifyUserScrollInput();

        Assert.Equal(PlayerAutoScrollState.ManualBrowsing, coordinator.State);
        Assert.True(coordinator.PendingRestoreVersion > firstVersion);

        timeProvider.Advance(TimeSpan.FromSeconds(2));
        Assert.False(coordinator.ShouldAutoCenter);

        timeProvider.Advance(TimeSpan.FromSeconds(2));
        Assert.True(coordinator.ShouldAutoCenter);
    }

    [Fact]
    public void Scrollbar_drag_blocks_restore_until_drag_completed()
    {
        var timeProvider = new ManualTimeProvider();
        var coordinator = new PlayerAutoScrollCoordinator(timeProvider);

        coordinator.BeginScrollbarDrag();
        timeProvider.Advance(TimeSpan.FromSeconds(10));

        Assert.Equal(PlayerAutoScrollState.ScrollbarDragging, coordinator.State);
        Assert.False(coordinator.ShouldAutoCenter);

        coordinator.EndScrollbarDrag();
        Assert.Equal(PlayerAutoScrollState.ManualBrowsing, coordinator.State);
        timeProvider.Advance(TimeSpan.FromSeconds(4));

        Assert.Equal(PlayerAutoScrollState.AutoCentering, coordinator.State);
        Assert.True(coordinator.ShouldAutoCenter);
    }

    [Fact]
    public void Passive_scroll_change_during_programmatic_scroll_is_ignored()
    {
        var timeProvider = new ManualTimeProvider();
        var coordinator = new PlayerAutoScrollCoordinator(timeProvider);

        coordinator.BeginProgrammaticScroll();
        coordinator.NotifyPassiveScrollChange();
        coordinator.EndProgrammaticScroll();

        Assert.True(coordinator.ShouldAutoCenter);
        Assert.False(coordinator.ShowReturnToCurrentSegment);
    }

    [Fact]
    public void Explicit_user_scroll_input_is_not_ignored_during_programmatic_scroll()
    {
        var coordinator = new PlayerAutoScrollCoordinator(new ManualTimeProvider());

        coordinator.BeginProgrammaticScroll();
        coordinator.NotifyUserScrollInput();
        coordinator.EndProgrammaticScroll();

        Assert.Equal(PlayerAutoScrollState.ManualBrowsing, coordinator.State);
        Assert.True(coordinator.ShowReturnToCurrentSegment);
    }

    [Fact]
    public void Resume_auto_center_cancels_pending_restore_and_returns_to_auto_center()
    {
        var timeProvider = new ManualTimeProvider();
        var coordinator = new PlayerAutoScrollCoordinator(timeProvider);

        coordinator.NotifyUserScrollInput();

        coordinator.ResumeAutoCenter();
        timeProvider.Advance(TimeSpan.FromSeconds(10));

        Assert.Equal(PlayerAutoScrollState.AutoCentering, coordinator.State);
        Assert.True(coordinator.ShouldAutoCenter);
        Assert.False(coordinator.ShowReturnToCurrentSegment);
    }

    [Fact]
    public void Page_leave_reset_cancels_pending_restore_and_returns_to_auto_center()
    {
        var timeProvider = new ManualTimeProvider();
        var coordinator = new PlayerAutoScrollCoordinator(timeProvider);

        coordinator.NotifyUserScrollInput();

        coordinator.ResetForPageLeave();
        timeProvider.Advance(TimeSpan.FromSeconds(10));

        Assert.Equal(PlayerAutoScrollState.AutoCentering, coordinator.State);
        Assert.True(coordinator.ShouldAutoCenter);
        Assert.False(coordinator.ShowReturnToCurrentSegment);
    }
}
