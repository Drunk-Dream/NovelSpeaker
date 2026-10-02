using NovelSpeaker.App.Shared.Presentation.Selection;
using Xunit;

namespace NovelSpeaker.App.PresentationTests.Selection;

public sealed class ManagementSelectionControllerTests
{
    [Fact]
    public void Explicit_entry_exit_and_page_reset_clear_selection_and_restore_normal_actions()
    {
        var controller = CreateController();
        var observedStates = new List<(bool ManagementMode, int Count)>();
        controller.StateChanged += (_, _) =>
            observedStates.Add((controller.IsManagementMode, controller.SelectedCount));

        controller.Enter();
        Assert.True(controller.IsManagementMode);
        Assert.Empty(controller.SelectedItems);
        controller.HandleClick("Bravo");
        Assert.True(controller.Exit());
        Assert.False(controller.IsManagementMode);
        Assert.Empty(controller.SelectedItems);
        Assert.False(controller.HandleClick("Bravo"));
        Assert.Contains((true, 0), observedStates);
        Assert.Equal((false, 0), observedStates[^1]);

        controller.Enter();
        controller.HandleClick("Delta", DesktopSelectionModifiers.Shift);
        Assert.Equal(["Delta"], controller.SelectedItems);
        controller.Reset();
        Assert.False(controller.IsManagementMode);
        Assert.Empty(controller.SelectedItems);
        controller.Enter();
        controller.SelectAll();
        Assert.Empty(controller.SelectedItems);
        Assert.Equal((true, 0), observedStates[^1]);
    }

    [Fact]
    public void Plain_clicks_in_management_mode_toggle_without_dispatching_the_normal_action()
    {
        var controller = CreateController();
        var normalActions = new List<string>();
        void Click(string item)
        {
            if (!controller.HandleClick(item)) normalActions.Add(item);
        }

        Click("Alpha");
        Assert.False(controller.IsManagementMode);
        Assert.Empty(controller.SelectedItems);

        controller.Enter();
        Click("Bravo");
        Click("Delta");
        Assert.Equal(["Bravo", "Delta"], controller.SelectedItems);
        Click("Bravo");
        Click("Delta");
        Assert.Empty(controller.SelectedItems);
        Assert.Equal(0, controller.SelectedCount);
        Assert.True(controller.IsManagementMode);
        Assert.Equal(["Alpha"], normalActions);

        controller.Exit();
        Click("Echo");
        Assert.Equal(["Alpha", "Echo"], normalActions);
    }

    [Theory]
    [InlineData(DesktopSelectionModifiers.Control)]
    [InlineData(DesktopSelectionModifiers.Shift)]
    [InlineData(DesktopSelectionModifiers.Control | DesktopSelectionModifiers.Shift)]
    public void Normal_modifier_click_enters_management_mode_and_selects_the_trigger_item(
        DesktopSelectionModifiers modifiers)
    {
        var controller = CreateController();

        Assert.True(controller.HandleClick("Bravo", modifiers));

        Assert.True(controller.IsManagementMode);
        Assert.Equal(["Bravo"], controller.SelectedItems);
    }

    [Fact]
    public void Management_modifiers_preserve_desktop_toggle_and_range_semantics()
    {
        var controller = CreateController();
        controller.HandleClick("Bravo", DesktopSelectionModifiers.Control);
        controller.HandleClick("Delta", DesktopSelectionModifiers.Shift);
        Assert.Equal(["Bravo", "Charlie", "Delta"], controller.SelectedItems);

        controller.HandleClick("Alpha", DesktopSelectionModifiers.Control | DesktopSelectionModifiers.Shift);
        Assert.Equal(["Alpha", "Bravo", "Charlie", "Delta"], controller.SelectedItems);

        controller.HandleClick("Charlie", DesktopSelectionModifiers.Control);
        Assert.Equal(["Alpha", "Bravo", "Delta"], controller.SelectedItems);
    }

    [Fact]
    public void Visible_set_refresh_preserves_stable_keys_but_hidden_items_never_return_selected()
    {
        var controller = CreateController();
        controller.Enter();
        controller.HandleClick("Bravo");
        controller.HandleClick("Delta");

        controller.SetItems(["Echo", "Delta", "Charlie", "Bravo", "Alpha"]);
        Assert.Equal(["Delta", "Bravo"], controller.SelectedItems);
        controller.SetItems(["Echo", "Bravo", "Charlie"]);
        Assert.Equal(["Bravo"], controller.SelectedItems);
        Assert.False(controller.IsSelected("Delta"));
        controller.SetItems(["Alpha", "Bravo", "Charlie", "Delta", "Echo"]);
        Assert.Equal(["Bravo"], controller.SelectedItems);

        controller.SetItems([]);
        Assert.Empty(controller.SelectedItems);
        Assert.True(controller.IsManagementMode);
        controller.SelectAll();
        Assert.Empty(controller.SelectedItems);
        Assert.True(controller.IsManagementMode);

        controller.SetItems(["Echo"]);
        controller.HandleClick("Echo", DesktopSelectionModifiers.Shift);
        Assert.Equal(["Echo"], controller.SelectedItems);
    }

    [Fact]
    public void Management_right_click_preserves_selected_items_or_replaces_with_the_target()
    {
        var controller = CreateController();
        Assert.False(controller.HandleRightClick("Bravo"));
        Assert.False(controller.IsManagementMode);
        Assert.Empty(controller.SelectedItems);

        controller.Enter();
        controller.HandleClick("Bravo");
        controller.HandleClick("Delta");
        Assert.True(controller.HandleRightClick("Bravo"));
        Assert.Equal(["Bravo", "Delta"], controller.SelectedItems);
        Assert.True(controller.HandleRightClick("Echo"));
        Assert.Equal(["Echo"], controller.SelectedItems);
    }

    [Fact]
    public void Select_all_uses_only_the_current_manageable_snapshot_without_visual_containers()
    {
        var controller = new ManagementSelectionController<int>();
        controller.SetItems(Enumerable.Range(0, 10_000));
        controller.SelectAll();
        Assert.False(controller.IsManagementMode);
        Assert.Empty(controller.SelectedItems);

        controller.Enter();
        controller.SelectAll();
        Assert.Equal(10_000, controller.SelectedCount);

        int[] visibleManageable = [9_999, 4_500];
        controller.SetIndexedItems(visibleManageable, new Dictionary<int, int> { [9_999] = 0, [4_500] = 1 });
        Assert.Equal(visibleManageable, controller.SelectedItems);
        controller.HandleClick(4_500);
        controller.SelectAll();
        Assert.Equal(visibleManageable, controller.SelectedItems);
        Assert.False(controller.IsSelected(0));
        Assert.Equal(2, controller.SelectedCount);
    }

    [Fact]
    public void Stale_management_gestures_are_consumed_without_selecting_hidden_items()
    {
        var controller = CreateController();
        Assert.True(controller.HandleClick("Removed", DesktopSelectionModifiers.Control));
        Assert.False(controller.IsManagementMode);
        Assert.Empty(controller.SelectedItems);

        controller.Enter();
        controller.HandleClick("Bravo");
        Assert.True(controller.HandleClick("Removed"));
        Assert.True(controller.HandleRightClick("Removed"));
        Assert.Equal(["Bravo"], controller.SelectedItems);
    }

    private static ManagementSelectionController<string> CreateController()
    {
        var controller = new ManagementSelectionController<string>(StringComparer.Ordinal);
        controller.SetItems(["Alpha", "Bravo", "Charlie", "Delta", "Echo"]);
        return controller;
    }
}
