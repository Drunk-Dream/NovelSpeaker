using System.Windows;
using System.Windows.Controls;
using Xunit;

namespace NovelSpeaker.App.WpfTests.Ui;

public sealed class RuleDragInteractionTests
{
    [Fact]
    public void Gesture_requires_hold_threshold_followed_by_movement()
    {
        var gesture = new RuleDragGestureStateMachine(
            minimumHorizontalDistance: 4,
            minimumVerticalDistance: 4);
        gesture.Press(new Point(10, 10), 1000, isExcludedRegion: false);

        Assert.False(gesture.ShouldBeginDrag(new Point(20, 20), 1299, isLeftButtonPressed: true));
        Assert.False(gesture.ShouldBeginDrag(new Point(12, 12), 1300, isLeftButtonPressed: true));
        Assert.True(gesture.ShouldBeginDrag(new Point(14, 10), 1300, isLeftButtonPressed: true));
        Assert.False(gesture.IsPressed);
    }

    [Fact]
    public void Gesture_excludes_toggle_and_cancels_when_button_is_released()
    {
        var gesture = new RuleDragGestureStateMachine();
        gesture.Press(new Point(), 0, isExcludedRegion: true);
        Assert.False(gesture.IsPressed);
        Assert.False(gesture.ShouldBeginDrag(new Point(20, 20), 500, isLeftButtonPressed: true));

        gesture.Press(new Point(), 1000, isExcludedRegion: false);
        Assert.False(gesture.ShouldBeginDrag(new Point(20, 20), 1400, isLeftButtonPressed: false));
        Assert.False(gesture.IsPressed);
    }

    [Fact]
    public void Placement_uses_target_vertical_center()
    {
        foreach (var (pointerY, targetHeight, expected) in new[]
                 {
                     (0d, 80d, RuleDropPlacement.Before),
                     (39.9d, 80d, RuleDropPlacement.Before),
                     (40d, 80d, RuleDropPlacement.After),
                     (79d, 80d, RuleDropPlacement.After)
                 })
        {
            Assert.Equal(expected, RuleDragGeometry.ResolvePlacement(pointerY, targetHeight));
        }
    }

    [Fact]
    public void Edge_scroll_direction_is_deterministic()
    {
        foreach (var (pointerY, expected) in new[]
                 {
                     (0d, -1),
                     (31d, -1),
                     (32d, 0),
                     (268d, 0),
                     (269d, 1),
                     (300d, 1)
                 })
        {
            Assert.Equal(expected, RuleDragGeometry.ResolveEdgeScrollDirection(pointerY, 300, 32));
        }
    }

    [Fact]
    public void Invalid_target_geometry_has_no_drop_placement()
    {
        foreach (var (pointerY, targetHeight) in new[]
                 {
                     (double.NaN, 80d),
                     (10d, 0d),
                     (10d, -1d)
                 })
        {
            Assert.Equal(RuleDropPlacement.None, RuleDragGeometry.ResolvePlacement(pointerY, targetHeight));
        }
    }

    [Fact]
    public void Logical_scrolling_uses_the_pixel_render_height_for_edge_detection()
    {
        WpfTestHost.RunInSta(() =>
        {
            var scrollViewer = new ScrollViewer
            {
                CanContentScroll = true,
                Content = new StackPanel()
            };
            scrollViewer.Measure(new Size(240, 300));
            scrollViewer.Arrange(new Rect(0, 0, 240, 300));

            Assert.Equal(300, scrollViewer.RenderSize.Height);
            Assert.Equal(0, RuleListItemView.ResolveEdgeScrollDirection(scrollViewer, 150));
            Assert.Equal(-1, RuleListItemView.ResolveEdgeScrollDirection(scrollViewer, 16));
            Assert.Equal(1, RuleListItemView.ResolveEdgeScrollDirection(scrollViewer, 284));
        });
    }
}
