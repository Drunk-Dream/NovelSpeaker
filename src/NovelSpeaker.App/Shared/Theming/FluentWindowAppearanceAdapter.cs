using Wpf.Ui.Controls;

namespace NovelSpeaker.App.Shared.Theming;

public sealed class FluentWindowAppearanceAdapter : IFluentWindowAppearanceAdapter
{
    public void SetExtendsContentIntoTitleBar(FluentWindow window, bool value)
    {
        window.ExtendsContentIntoTitleBar = value;
    }

    public void SetBackdrop(FluentWindow window, WindowBackdropType backdropType)
    {
        window.WindowBackdropType = backdropType;
    }
}
