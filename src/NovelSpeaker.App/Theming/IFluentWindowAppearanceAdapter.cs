using Wpf.Ui.Controls;

namespace NovelSpeaker.App.Theming;

public interface IFluentWindowAppearanceAdapter
{
    void SetExtendsContentIntoTitleBar(FluentWindow window, bool value);

    void SetBackdrop(FluentWindow window, WindowBackdropType backdropType);
}
