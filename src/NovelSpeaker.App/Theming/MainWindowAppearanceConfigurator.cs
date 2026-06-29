using Wpf.Ui.Controls;

namespace NovelSpeaker.App.Theming;

public sealed class MainWindowAppearanceConfigurator : IMainWindowAppearanceConfigurator
{
    private readonly IFluentWindowAppearanceAdapter _appearanceAdapter;

    public MainWindowAppearanceConfigurator(IFluentWindowAppearanceAdapter appearanceAdapter)
    {
        _appearanceAdapter = appearanceAdapter;
    }

    public void Configure(FluentWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        _appearanceAdapter.SetExtendsContentIntoTitleBar(window, true);

        try
        {
            _appearanceAdapter.SetBackdrop(window, WindowBackdropType.Mica);
        }
        catch
        {
            _appearanceAdapter.SetBackdrop(window, WindowBackdropType.None);
        }
    }
}
