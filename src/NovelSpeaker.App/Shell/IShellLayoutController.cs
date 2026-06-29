using System;

namespace NovelSpeaker.App.Shell;

public interface IShellLayoutController
{
    bool IsPaneOpen { get; }

    event EventHandler<bool>? PaneStateChanged;

    void UpdateWindowWidth(double width);

    void HandlePaneStateChanged(bool isPaneOpen);
}
