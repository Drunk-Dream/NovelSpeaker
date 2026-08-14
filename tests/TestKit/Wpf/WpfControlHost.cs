using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NovelSpeaker.TestKit.Wpf;

internal sealed class WpfControlHost : IDisposable
{
    public WpfControlHost(FrameworkElement root)
    {
        Root = root ?? throw new ArgumentNullException(nameof(root));
        WpfTestHost.RegisterDiagnosticRoot(root);
    }

    public FrameworkElement Root { get; }

    public void MeasureArrange(Size size)
    {
        Root.Measure(size);
        Root.Arrange(new Rect(new Point(0, 0), size));
        Root.ApplyTemplate();
        Root.UpdateLayout();
    }

    public RenderTargetBitmap Render(Size size, double dpi = 96)
    {
        MeasureArrange(size);

        var bitmap = new RenderTargetBitmap(
            Math.Max(1, (int)Math.Ceiling(size.Width * dpi / 96)),
            Math.Max(1, (int)Math.Ceiling(size.Height * dpi / 96)),
            dpi,
            dpi,
            PixelFormats.Pbgra32);
        bitmap.Render(Root);
        return bitmap;
    }

    // The test host clears roots after it has captured failure diagnostics. This
    // keeps a disposed control available when an assertion fails during unwind.
    public void Dispose()
    {
    }
}
