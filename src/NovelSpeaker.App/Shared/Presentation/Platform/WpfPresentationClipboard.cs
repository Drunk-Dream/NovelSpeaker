using System.Windows;

namespace NovelSpeaker.App.Shared.Presentation.Platform;

public sealed class WpfPresentationClipboard(IUiScheduler uiScheduler) : IPresentationClipboard
{
    public async Task<string?> GetTextAsync(CancellationToken cancellationToken)
    {
        string? text = null;
        await uiScheduler.InvokeAsync(
            () => text = Clipboard.ContainsText() ? Clipboard.GetText() : null,
            cancellationToken);
        return text;
    }

    public Task SetTextAsync(string text, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        return uiScheduler.InvokeAsync(() => Clipboard.SetText(text), cancellationToken);
    }
}
