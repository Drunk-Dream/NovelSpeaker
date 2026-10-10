using NAudio.Wave;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Infrastructure.Playback;
using Xunit;

namespace NovelSpeaker.Infrastructure.IntegrationTests;

public sealed class NaudioAudioPlayerTests
{
    [Fact]
    public async Task Playback_completion_and_replacement_release_decoded_audio_resources()
    {
        var firstPath = CopyToTemporaryFile(PlaybackTestAudio.DemoWavPath);
        var secondPath = CopyToTemporaryFile(PlaybackTestAudio.DemoMp3Path);
        var wavePlayer = new FakeWavePlayer();
        var player = new NaudioAudioPlayer(() => wavePlayer);
        var completionCount = 0;
        player.PlaybackCompleted += (_, _) => completionCount++;

        try
        {
            await player.LoadAsync(firstPath, CancellationToken.None);
            await player.LoadAsync(secondPath, CancellationToken.None);
            Assert.True(wavePlayer.OutputWaveFormat is { SampleRate: 44100, Channels: 2 });

            using (new FileStream(firstPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }

            player.Seek(player.Duration);
            player.Play();
            wavePlayer.RaisePlaybackStopped();

            Assert.Equal(1, completionCount);
            Assert.Equal(NovelSpeaker.Application.Playback.PlaybackState.Stopped, player.State);

            await player.DisposeAsync();
            using (new FileStream(secondPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            Assert.True(wavePlayer.IsDisposed);
        }
        finally
        {
            await player.DisposeAsync();
            File.Delete(firstPath);
            File.Delete(secondPath);
        }
    }

    private static string CopyToTemporaryFile(string sourcePath)
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}{Path.GetExtension(sourcePath)}");
        File.Copy(sourcePath, filePath);
        return filePath;
    }

    private sealed class FakeWavePlayer : IWavePlayer
    {
        public bool IsDisposed { get; private set; }
        public NAudio.Wave.PlaybackState PlaybackState { get; private set; } = NAudio.Wave.PlaybackState.Stopped;
        public WaveFormat? OutputWaveFormat => WaveProvider?.WaveFormat;
        public float Volume { get; set; } = 1f;
        private IWaveProvider? WaveProvider { get; set; }
        public event EventHandler<StoppedEventArgs>? PlaybackStopped;

        public void Init(IWaveProvider waveProvider) => WaveProvider = waveProvider;
        public void Play() => PlaybackState = NAudio.Wave.PlaybackState.Playing;
        public void Pause() => PlaybackState = NAudio.Wave.PlaybackState.Paused;
        public void Stop()
        {
            PlaybackState = NAudio.Wave.PlaybackState.Stopped;
            PlaybackStopped?.Invoke(this, new StoppedEventArgs());
        }

        public void RaisePlaybackStopped()
        {
            PlaybackState = NAudio.Wave.PlaybackState.Stopped;
            PlaybackStopped?.Invoke(this, new StoppedEventArgs());
        }

        public void Dispose() => IsDisposed = true;
    }
}
