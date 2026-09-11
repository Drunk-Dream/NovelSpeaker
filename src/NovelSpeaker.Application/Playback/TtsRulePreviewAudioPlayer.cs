using NovelSpeaker.Application.Playback.Audio;
using NovelSpeaker.Application.Speech.Testing;

namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Adapts the local audio player to the narrow Speech rule-preview role.
/// </summary>
public sealed class TtsRulePreviewAudioPlayer : ITtsRulePreviewAudioPlayer
{
    private readonly IAudioPlayer _audioPlayer;
    private bool _disposed;

    public TtsRulePreviewAudioPlayer(IAudioPlayerFactory audioPlayerFactory)
    {
        _audioPlayer = audioPlayerFactory.Create();
    }

    public async Task<TtsRulePreviewPlaybackResult> PlayAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            _audioPlayer.Stop();
            await _audioPlayer.LoadAsync(filePath, cancellationToken).ConfigureAwait(false);
            _audioPlayer.Play();
            return new TtsRulePreviewPlaybackResult(true, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new TtsRulePreviewPlaybackResult(
                false,
                PlaybackErrorMapper.Map(exception).Message,
                exception);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _audioPlayer.DisposeAsync().ConfigureAwait(false);
    }
}
