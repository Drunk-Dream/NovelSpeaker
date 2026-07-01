namespace NovelSpeaker.App.Player;

public interface IPlayerLayoutController
{
    bool IsCompactLayout { get; }

    bool IsDrawerOpen { get; }

    event EventHandler? StateChanged;

    void UpdateWidth(double width);

    void OpenDrawer();

    void CloseDrawer();

    void ToggleDrawer();
}
