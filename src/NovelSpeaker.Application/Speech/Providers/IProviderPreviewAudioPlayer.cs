namespace NovelSpeaker.Application.Speech.Providers;

/// <summary>Plays a synthesized preview without entering the chapter playback or cache flows.</summary>
public interface IProviderPreviewAudioPlayer : IAsyncDisposable
{
    event EventHandler<ProviderPreviewPlaybackFailedEventArgs>? PlaybackFailed;

    Task StopAsync(CancellationToken cancellationToken);

    Task<ProviderPreviewPlaybackResult> PlayAsync(
        Stream audio,
        string? audioFormat,
        CancellationToken cancellationToken);
}

public sealed record ProviderPreviewPlaybackResult(bool IsSuccess, string? FailureMessage);

public sealed class ProviderPreviewPlaybackFailedEventArgs(string message) : EventArgs
{
    public string Message { get; } = message;
}
