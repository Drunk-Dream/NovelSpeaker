using System.Windows;

namespace NovelSpeaker.App.Diagnostics;

public sealed class WpfClipboardService : IClipboardService
{
    public void SetText(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            Clipboard.SetText(text);
            return;
        }

        dispatcher.Invoke(() => Clipboard.SetText(text));
    }
}
