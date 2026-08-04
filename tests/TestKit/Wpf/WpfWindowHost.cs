using System.Windows;

namespace NovelSpeaker.TestKit.Wpf;

internal sealed class WpfWindowHost : IDisposable
{
    private readonly bool _ownsWindow;

    public WpfWindowHost(Window window, bool ownsWindow = false)
    {
        Window = window ?? throw new ArgumentNullException(nameof(window));
        _ownsWindow = ownsWindow;
        WpfTestHost.RegisterWindow(window);
        WpfTestHost.RegisterDiagnosticRoot(window);
    }

    public Window Window { get; }

    public static WpfWindowHost Show(Window window)
    {
        var host = new WpfWindowHost(window);
        host.Show();
        return host;
    }

    public static WpfWindowHost ForControl(FrameworkElement root)
    {
        return new WpfWindowHost(new Window { Content = root }, ownsWindow: true);
    }

    public void Show()
    {
        ApplyTestWindowConfiguration();
        RoutedEventHandler? loaded = null;
        loaded = (_, _) =>
        {
            ApplyTestWindowConfiguration();
            WpfTestHost.RegisterWindow(Window);
            Window.Loaded -= loaded;
        };
        Window.Loaded += loaded;

        Window.Show();

        // MainWindow applies its platform defaults during Loaded; restore the
        // test-host boundary after that lifecycle hook has run as well.
        ApplyTestWindowConfiguration();
        WpfTestHost.RegisterWindow(Window);
    }

    private void ApplyTestWindowConfiguration()
    {
        Window.ShowInTaskbar = false;
        Window.ShowActivated = false;
        Window.WindowStartupLocation = WindowStartupLocation.Manual;

        if (string.Equals(
                Environment.GetEnvironmentVariable("NOVELSPEAKER_TEST_SHOW_WINDOWS"),
                "1",
                StringComparison.Ordinal))
        {
            Window.Left = 120;
            Window.Top = 120;
            return;
        }

        var width = Window.ActualWidth > 0 ? Window.ActualWidth : Window.Width;
        var height = Window.ActualHeight > 0 ? Window.ActualHeight : Window.Height;
        Window.Left = SystemParameters.VirtualScreenLeft - Math.Max(width, 1) - 100;
        Window.Top = SystemParameters.VirtualScreenTop - Math.Max(height, 1) - 100;
    }

    public void Dispose()
    {
        if (Window.IsVisible)
        {
            Window.Close();
        }

        Window.Content = null;
        if (_ownsWindow)
        {
            Window.ClearValue(FrameworkElement.DataContextProperty);
        }

    }
}
