using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Speech.Providers;

namespace NovelSpeaker.Infrastructure.Speech.Http;

/// <summary>Owns the isolated local player and temporary file for Provider previews.</summary>
internal sealed class ProviderPreviewAudioPlayer : IProviderPreviewAudioPlayer
{
    private readonly IAudioPlayerFactory _playerFactory;
    private IAudioPlayer? _player;
    private readonly IAppDataDirectoryProvider _directories;
    private readonly IAppStoragePathResolver _paths;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _currentPath;
    private bool _disposed;

    public ProviderPreviewAudioPlayer(
        IAudioPlayerFactory playerFactory,
        IAppDataDirectoryProvider directories,
        IAppStoragePathResolver paths)
    {
        _playerFactory = playerFactory;
        _directories = directories;
        _paths = paths;
    }

    public event EventHandler<ProviderPreviewPlaybackFailedEventArgs>? PlaybackFailed;

    public async Task<ProviderPreviewPlaybackResult> PlayAsync(
        Stream audio,
        string? audioFormat,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(audio);
        if (audioFormat is not ("mp3" or "wav"))
        {
            return new ProviderPreviewPlaybackResult(false, "试听音频格式不可用。");
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        string? candidate = null;
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var directory = _paths.ResolvePath(Path.Combine(_directories.CacheDirectoryPath, "ProviderPreviews"));
            Directory.CreateDirectory(directory);
            candidate = _paths.ResolvePath(Path.Combine(directory, $"preview-{Guid.NewGuid():N}.{audioFormat}"));
            await using (var file = new FileStream(candidate, FileMode.CreateNew, FileAccess.Write,
                             FileShare.None, 4096, FileOptions.Asynchronous))
            {
                await audio.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var player = GetPlayer();
            await player.LoadAsync(candidate, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            player.Play();
            var previous = _currentPath;
            _currentPath = candidate;
            candidate = null;
            TemporaryAudioStore.Delete(previous);
            return new ProviderPreviewPlaybackResult(true, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await ResetPlayerSafelyAsync().ConfigureAwait(false);
            throw;
        }
        catch (Exception)
        {
            await ResetPlayerSafelyAsync().ConfigureAwait(false);
            return new ProviderPreviewPlaybackResult(false, "本地试听播放失败。");
        }
        finally
        {
            TemporaryAudioStore.Delete(candidate);
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            await ResetPlayerSafelyAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private IAudioPlayer GetPlayer()
    {
        if (_player is null)
        {
            _player = _playerFactory.Create();
            _player.PlaybackFailed += OnPlaybackFailed;
        }

        return _player;
    }

    private void OnPlaybackFailed(object? sender, PlaybackErrorEventArgs eventArgs) =>
        PlaybackFailed?.Invoke(this, new ProviderPreviewPlaybackFailedEventArgs("试听播放中断，请检查音频设备。"));

    private async Task ResetPlayerSafelyAsync()
    {
        var player = _player;
        _player = null;
        try
        {
            if (player is not null)
            {
                player.PlaybackFailed -= OnPlaybackFailed;
                await player.DisposeAsync().ConfigureAwait(false);
            }
        }
        catch
        {
            // Cleanup failures must not replace cancellation or local playback errors.
        }
        finally
        {
            TemporaryAudioStore.Delete(_currentPath);
            _currentPath = null;
        }
    }
}
