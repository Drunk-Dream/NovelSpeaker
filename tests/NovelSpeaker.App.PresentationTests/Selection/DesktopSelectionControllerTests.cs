using NovelSpeaker.App.Shared.Presentation.Selection;
using Xunit;

namespace NovelSpeaker.App.PresentationTests.Selection;

public sealed class DesktopSelectionControllerTests
{
    [Fact]
    public void Click_replaces_selection_and_updates_anchor_and_primary_item()
    {
        var controller = CreateController();

        controller.Click("Bravo");
        controller.Click("Delta");

        Assert.Equal(["Delta"], controller.SelectedItems);
        Assert.Equal("Delta", controller.AnchorItem);
        Assert.Equal("Delta", controller.PrimaryItem);
    }

    [Fact]
    public void Control_click_adds_and_removes_items_without_using_visual_containers()
    {
        var controller = CreateController();

        controller.Click("Alpha");
        controller.Click("Charlie", DesktopSelectionModifiers.Control);
        controller.Click("Charlie", DesktopSelectionModifiers.Control);

        Assert.Equal(["Alpha"], controller.SelectedItems);
        Assert.True(controller.IsSelected("Alpha"));
        Assert.False(controller.IsSelected("Charlie"));
        Assert.Equal("Charlie", controller.AnchorItem);
        Assert.Equal("Alpha", controller.PrimaryItem);
    }

    [Fact]
    public void Shift_click_selects_anchor_range_and_control_shift_adds_a_range()
    {
        var controller = CreateController();

        controller.Click("Bravo");
        controller.Click("Delta", DesktopSelectionModifiers.Shift);

        Assert.Equal(["Bravo", "Charlie", "Delta"], controller.SelectedItems);
        Assert.Equal("Bravo", controller.AnchorItem);
        Assert.Equal("Delta", controller.PrimaryItem);

        controller.Click("Alpha", DesktopSelectionModifiers.Control | DesktopSelectionModifiers.Shift);

        Assert.Equal(["Alpha", "Bravo", "Charlie", "Delta"], controller.SelectedItems);
        Assert.Equal("Bravo", controller.AnchorItem);
        Assert.Equal("Alpha", controller.PrimaryItem);
    }

    [Fact]
    public void Select_all_and_escape_clear_selection_metadata()
    {
        var controller = CreateController();

        controller.SelectAll();

        Assert.Equal(["Alpha", "Bravo", "Charlie", "Delta", "Echo"], controller.SelectedItems);
        Assert.Equal("Alpha", controller.AnchorItem);
        Assert.Equal("Alpha", controller.PrimaryItem);

        controller.Clear();

        Assert.Empty(controller.SelectedItems);
        Assert.False(controller.HasAnchor);
        Assert.False(controller.HasPrimary);
    }

    [Fact]
    public void Replacing_items_preserves_live_keys_and_reconciles_removed_metadata()
    {
        var controller = CreateController();
        controller.Click("Bravo");
        controller.Click("Delta", DesktopSelectionModifiers.Control);

        controller.SetItems(["Echo", "Bravo", "Charlie"]);

        Assert.Equal(["Bravo"], controller.SelectedItems);
        Assert.Equal("Bravo", controller.AnchorItem);
        Assert.Equal("Bravo", controller.PrimaryItem);
        Assert.False(controller.IsSelected("Delta"));
    }

    [Fact]
    public void Logical_selection_survives_virtualized_container_recycling_and_ignores_stale_clicks()
    {
        var controller = new DesktopSelectionController<int>();
        controller.SetItems(Enumerable.Range(0, 10_000));

        controller.Click(4_500);
        controller.Click(9_999, DesktopSelectionModifiers.Shift);
        controller.SetItems(Enumerable.Range(4_000, 6_000));
        controller.Click(25_000, DesktopSelectionModifiers.Control);

        Assert.Equal(5_500, controller.Count);
        Assert.True(controller.IsSelected(4_500));
        Assert.True(controller.IsSelected(9_999));
        Assert.False(controller.IsSelected(4_499));
        Assert.Equal(4_500, controller.AnchorItem);
        Assert.Equal(9_999, controller.PrimaryItem);
    }

    [Fact]
    public void Set_items_rejects_duplicate_keys_because_ranges_must_be_unambiguous()
    {
        var controller = new DesktopSelectionController<string>();

        Assert.Throws<ArgumentException>(() => controller.SetItems(["Alpha", "Alpha"]));
    }

    private static DesktopSelectionController<string> CreateController()
    {
        var controller = new DesktopSelectionController<string>();
        controller.SetItems(["Alpha", "Bravo", "Charlie", "Delta", "Echo"]);
        return controller;
    }
}
