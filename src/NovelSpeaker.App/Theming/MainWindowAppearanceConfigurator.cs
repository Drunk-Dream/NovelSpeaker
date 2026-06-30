using Microsoft.Extensions.Logging;
using System.Windows;
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

    public void Configure(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (window is not FluentWindow fluentWindow)
        {
            return;
        }

        try
        {
            _appearanceAdapter.SetBackdrop(fluentWindow, WindowBackdropType.Mica);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to enable Mica backdrop for the main window. Falling back to the default window chrome.");
        }
    }
}
