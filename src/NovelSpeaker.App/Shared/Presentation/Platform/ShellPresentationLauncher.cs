using System.Diagnostics;

namespace NovelSpeaker.App.Shared.Presentation.Platform;

public sealed class ShellPresentationLauncher : IPresentationLauncher
{
    public Task OpenAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        cancellationToken.ThrowIfCancellationRequested();
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
        return Task.CompletedTask;
    }
}
