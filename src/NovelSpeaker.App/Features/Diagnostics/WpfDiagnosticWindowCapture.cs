using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NovelSpeaker.Application.Diagnostics;

namespace NovelSpeaker.App.Features.Diagnostics;

internal sealed class WpfDiagnosticWindowCapture : IDiagnosticWindowCapture
{
    private readonly Func<Window?> _windowProvider;
    private readonly TimeProvider _timeProvider;

    public WpfDiagnosticWindowCapture(TimeProvider timeProvider)
        : this(() => System.Windows.Application.Current?.MainWindow, timeProvider)
    {
    }

    public WpfDiagnosticWindowCapture(Func<Window?> windowProvider, TimeProvider timeProvider)
    {
        _windowProvider = windowProvider ?? throw new ArgumentNullException(nameof(windowProvider));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public Task<DiagnosticAttachment> CaptureCurrentWindowAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var window = _windowProvider() ?? throw new InvalidOperationException("当前没有可截取的 NovelSpeaker 窗口。");
        if (!window.IsVisible || window.ActualWidth <= 0 || window.ActualHeight <= 0)
        {
            throw new InvalidOperationException("NovelSpeaker 窗口当前不可见。");
        }

        var dpi = VisualTreeHelper.GetDpi(window);
        var width = Math.Max(1, (int)Math.Ceiling(window.ActualWidth * dpi.DpiScaleX));
        var height = Math.Max(1, (int)Math.Ceiling(window.ActualHeight * dpi.DpiScaleY));
        var bitmap = new RenderTargetBitmap(width, height, 96d * dpi.DpiScaleX, 96d * dpi.DpiScaleY, PixelFormats.Pbgra32);
        bitmap.Render(window);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        var content = stream.ToArray();
        var attachment = new DiagnosticAttachment(
            "capture-" + Guid.NewGuid().ToString("N"),
            _timeProvider.GetUtcNow().ToUniversalTime(),
            "image/png",
            width,
            height,
            content);
        return Task.FromResult(attachment);
    }
}
