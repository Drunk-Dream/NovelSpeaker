using Microsoft.Extensions.Logging;
using Wpf.Ui.Controls;

namespace NovelSpeaker.App.Theming;

public sealed class MainWindowAppearanceConfigurator : IMainWindowAppearanceConfigurator
{
    private readonly IFluentWindowAppearanceAdapter _appearanceAdapter;
    private readonly ILogger<MainWindowAppearanceConfigurator> _logger;

    public MainWindowAppearanceConfigurator(
        IFluentWindowAppearanceAdapter appearanceAdapter,
        ILogger<MainWindowAppearanceConfigurator> logger)
    {
        _appearanceAdapter = appearanceAdapter;
        _logger = logger;
    }

    public void Configure(FluentWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        _appearanceAdapter.SetExtendsContentIntoTitleBar(window, true);

        try
        {
            _appearanceAdapter.SetBackdrop(window, WindowBackdropType.Mica);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to enable Mica backdrop for the main window. Falling back to the default window chrome.");
        }
    }
}
