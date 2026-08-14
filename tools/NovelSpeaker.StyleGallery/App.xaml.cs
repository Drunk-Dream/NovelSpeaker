using System.Windows;
using System.Windows.Threading;

namespace NovelSpeaker.StyleGallery;

public partial class GalleryApp : System.Windows.Application
{
    public GalleryApp()
    {
        GalleryDpiAwareness.TryEnableFixedDpi();
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnCurrentDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var options = GalleryCommandLineOptions.Parse(e.Args);
        GalleryThemeRuntime.EnsureProviderResources();
        GalleryThemeRuntime.Apply(options.Theme.ToGalleryTheme());

        var window = new GalleryWindow();
        MainWindow = window;

        try
        {
            if (options.ScreenshotMode)
            {
                window.Show();
                await window.GenerateScreenshotsAsync(options);
                Shutdown(0);
            }
            else
            {
                window.Show();
            }
        }
        catch (OperationCanceledException)
        {
            Shutdown(2);
        }
        catch (Exception)
        {
            Shutdown(1);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        Shutdown(1);
    }

    private void OnCurrentDomainUnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        if (!e.IsTerminating)
        {
            Shutdown(1);
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        e.SetObserved();
        Shutdown(1);
    }
}
