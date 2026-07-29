using NAudio.Wave;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Infrastructure.Playback;
using PlaybackStatus = NovelSpeaker.Application.Playback.PlaybackState;
using Xunit;

namespace NovelSpeaker.Infrastructure.IntegrationTests;

public sealed class NaudioAudioPlayerTests
{
    [Theory]
    [InlineData("wav")]
    [InlineData("mp3")]
    public async Task LoadAsync_reads_duration_for_supported_audio(string extension)
    {
        var wavePlayer = new FakeWavePlayer();
        await using var player = new NaudioAudioPlayer(() => wavePlayer);

        var filePath = extension == "wav" ? PlaybackTestAudio.DemoWavPath : PlaybackTestAudio.DemoMp3Path;
        await player.LoadAsync(filePath, CancellationToken.None);

        Assert.Equal(PlaybackStatus.Stopped, player.State);
        Assert.True(player.Duration > TimeSpan.Zero);
        Assert.NotNull(wavePlayer.WaveProvider);
        Assert.Equal(44100, wavePlayer.OutputWaveFormat?.SampleRate);
        Assert.Equal(2, wavePlayer.OutputWaveFormat?.Channels);
    }

    [Fact]
    public async Task Play_pause_stop_and_seek_update_local_state()
    {
        var wavePlayer = new FakeWavePlayer();
        await using var player = new NaudioAudioPlayer(() => wavePlayer);

        await player.LoadAsync(PlaybackTestAudio.DemoWavPath, CancellationToken.None);
        player.Play();
        Assert.Equal(PlaybackStatus.Playing, player.State);

        player.Seek(TimeSpan.FromDays(1));
        Assert.Equal(player.Duration, player.Position);

        player.Pause();
        Assert.Equal(PlaybackStatus.Paused, player.State);

        player.Stop();
        Assert.Equal(PlaybackStatus.Stopped, player.State);
        Assert.Equal(TimeSpan.Zero, player.Position);
    }

    [Fact]
    public async Task Volume_is_applied_to_wave_output_without_changing_system_volume()
    {
        var wavePlayer = new FakeWavePlayer();
        await using var player = new NaudioAudioPlayer(() => wavePlayer);

        player.Volume = 0.4;
        await player.LoadAsync(PlaybackTestAudio.DemoWavPath, CancellationToken.None);

        Assert.Equal(0.4, player.Volume);
        Assert.Equal(0.4f, wavePlayer.Volume);

        player.Volume = 2;
        Assert.Equal(1, player.Volume);
        Assert.Equal(1f, wavePlayer.Volume);
    }

    [Fact]
    public async Task PlaybackStopped_at_end_raises_completion_event()
    {
        var wavePlayer = new FakeWavePlayer();
        await using var player = new NaudioAudioPlayer(() => wavePlayer);
        var completed = false;
        player.PlaybackCompleted += (_, _) => completed = true;

        await player.LoadAsync(PlaybackTestAudio.DemoWavPath, CancellationToken.None);
        player.Seek(player.Duration);
        player.Play();
        wavePlayer.RaisePlaybackStopped();

        Assert.True(completed);
        Assert.Equal(PlaybackStatus.Stopped, player.State);
    }

    [Fact]
    public async Task PlaybackStopped_before_exact_end_still_raises_completion_event()
    {
        var wavePlayer = new FakeWavePlayer();
        await using var player = new NaudioAudioPlayer(() => wavePlayer);
        var completed = false;
        player.PlaybackCompleted += (_, _) => completed = true;

        await player.LoadAsync(PlaybackTestAudio.DemoMp3Path, CancellationToken.None);
        player.Play();
        wavePlayer.RaisePlaybackStopped();

        Assert.True(completed);
        Assert.Equal(PlaybackStatus.Stopped, player.State);
    }

    [Fact]
    public async Task LoadAsync_raises_failed_event_for_missing_file()
    {
        var wavePlayer = new FakeWavePlayer();
        await using var player = new NaudioAudioPlayer(() => wavePlayer);
        PlaybackErrorEventArgs? captured = null;
        player.PlaybackFailed += (_, error) => captured = error;

        await Assert.ThrowsAnyAsync<Exception>(() => player.LoadAsync("missing-file.mp3", CancellationToken.None));

        Assert.NotNull(captured);
        Assert.Equal(PlaybackErrorKind.FileNotFound, captured!.Kind);
    }

    [Fact]
    public async Task LoadAsync_raises_failed_event_for_corrupt_audio()
    {
        var wavePlayer = new FakeWavePlayer();
        await using var player = new NaudioAudioPlayer(() => wavePlayer);
        PlaybackErrorEventArgs? captured = null;
        player.PlaybackFailed += (_, error) => captured = error;

        await Assert.ThrowsAnyAsync<Exception>(() => player.LoadAsync(PlaybackTestAudio.CorruptMp3Path, CancellationToken.None));

        Assert.NotNull(captured);
        Assert.Equal(PlaybackStatus.Faulted, player.State);
        Assert.Contains(captured!.Kind, new[] { PlaybackErrorKind.UnsupportedFormat, PlaybackErrorKind.AudioDecode, PlaybackErrorKind.Unknown });
    }

    [Fact]
    public async Task Extremely_short_wav_loads_and_raises_completion_once()
    {
        var wavePlayer = new FakeWavePlayer();
        await using var player = new NaudioAudioPlayer(() => wavePlayer);
        var filePath = CreateShortWaveFile();
        var completionCount = 0;
        player.PlaybackCompleted += (_, _) => completionCount++;

        await player.LoadAsync(filePath, CancellationToken.None);
        player.Seek(player.Duration);
        player.Play();
        wavePlayer.RaisePlaybackStopped();

        Assert.Equal(1, completionCount);
        Assert.Equal(PlaybackStatus.Stopped, player.State);
    }

    [Fact]
    public async Task Stop_can_be_called_repeatedly_without_raising_completion()
    {
        var wavePlayer = new FakeWavePlayer();
        await using var player = new NaudioAudioPlayer(() => wavePlayer);
        var completionCount = 0;
        player.PlaybackCompleted += (_, _) => completionCount++;

        await player.LoadAsync(PlaybackTestAudio.DemoWavPath, CancellationToken.None);
        player.Play();
        player.Stop();
        player.Stop();

        Assert.Equal(PlaybackStatus.Stopped, player.State);
        Assert.Equal(TimeSpan.Zero, player.Position);
        Assert.Equal(0, completionCount);
    }

    [Fact]
    public async Task Loading_next_audio_reuses_existing_output_device()
    {
        var factory = new FakeWavePlayerFactory();
        await using var player = new NaudioAudioPlayer(factory.Create);

        await player.LoadAsync(PlaybackTestAudio.DemoWavPath, CancellationToken.None);
        player.Play();
        await player.LoadAsync(PlaybackTestAudio.DemoMp3Path, CancellationToken.None);
        player.Play();

        Assert.Single(factory.CreatedPlayers);
        Assert.Equal(PlaybackStatus.Playing, player.State);
    }

    [Fact]
    public async Task Suppressed_stop_during_reload_does_not_raise_completion_for_next_audio()
    {
        var wavePlayer = new FakeWavePlayer();
        await using var player = new NaudioAudioPlayer(() => wavePlayer);
        var completionCount = 0;
        player.PlaybackCompleted += (_, _) => completionCount++;

        await player.LoadAsync(PlaybackTestAudio.DemoWavPath, CancellationToken.None);
        player.Play();
        await player.LoadAsync(PlaybackTestAudio.DemoMp3Path, CancellationToken.None);
        player.Play();

        Assert.Equal(0, completionCount);
        wavePlayer.RaisePlaybackStopped();

        Assert.Equal(1, completionCount);
        Assert.Equal(PlaybackStatus.Stopped, player.State);
    }

    [Fact]
    public async Task LoadAsync_releases_output_device_when_initialization_fails()
    {
        var wavePlayer = new ThrowingInitWavePlayer();
        var player = new NaudioAudioPlayer(() => wavePlayer);
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                player.LoadAsync(PlaybackTestAudio.DemoWavPath, CancellationToken.None));

            Assert.True(wavePlayer.IsDisposed);
        }
        finally
        {
            await player.DisposeAsync();
        }
    }

    [Fact]
    public async Task LoadAsync_releases_reader_when_format_conversion_fails()
    {
        var filePath = CreateThreeChannelWaveFile();
        await using var player = new NaudioAudioPlayer(() => new FakeWavePlayer());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            player.LoadAsync(filePath, CancellationToken.None));

        using var exclusive = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None);
    }

    [Fact]
    public async Task Replacing_audio_releases_previous_reader_and_dispose_releases_current_reader()
    {
        var firstPath = CopyToTemporaryFile(PlaybackTestAudio.DemoWavPath);
        var secondPath = CopyToTemporaryFile(PlaybackTestAudio.DemoMp3Path);
        var wavePlayer = new FakeWavePlayer();
        var player = new NaudioAudioPlayer(() => wavePlayer);

        await player.LoadAsync(firstPath, CancellationToken.None);
        await player.LoadAsync(secondPath, CancellationToken.None);

        using (new FileStream(firstPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
        }

        await player.DisposeAsync();

        using (new FileStream(secondPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
        }
        Assert.True(wavePlayer.IsDisposed);
    }

    [Fact]
    public async Task Play_failure_releases_reader_and_output_device()
    {
        var filePath = CopyToTemporaryFile(PlaybackTestAudio.DemoWavPath);
        var wavePlayer = new ThrowingPlayWavePlayer();
        var player = new NaudioAudioPlayer(() => wavePlayer);
        await player.LoadAsync(filePath, CancellationToken.None);

        Assert.Throws<InvalidOperationException>(player.Play);

        using (new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
        }
        Assert.True(wavePlayer.IsDisposed);
        await player.DisposeAsync();
    }

    private static string CreateShortWaveFile()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.wav");
        using var writer = new WaveFileWriter(filePath, new WaveFormat(8000, 16, 1));
        writer.WriteSample(0.1f);
        return filePath;
    }

    private static string CreateThreeChannelWaveFile()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.wav");
        using var writer = new WaveFileWriter(filePath, new WaveFormat(44100, 16, 3));
        writer.Write(new byte[6], 0, 6);
        return filePath;
    }

    private static string CopyToTemporaryFile(string sourcePath)
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}{Path.GetExtension(sourcePath)}");
        File.Copy(sourcePath, filePath);
        return filePath;
    }

    private sealed class ThrowingInitWavePlayer : IWavePlayer
    {
        public bool IsDisposed { get; private set; }
        public NAudio.Wave.PlaybackState PlaybackState => NAudio.Wave.PlaybackState.Stopped;
        public WaveFormat OutputWaveFormat => new(44100, 16, 2);
        public float Volume { get; set; }
        public event EventHandler<StoppedEventArgs>? PlaybackStopped
        {
            add { }
            remove { }
        }
        public void Init(IWaveProvider waveProvider) => throw new InvalidOperationException("init failed");
        public void Play() { }
        public void Pause() { }
        public void Stop() { }
        public void Dispose() => IsDisposed = true;
    }

    private sealed class ThrowingPlayWavePlayer : IWavePlayer
    {
        public bool IsDisposed { get; private set; }
        public NAudio.Wave.PlaybackState PlaybackState => NAudio.Wave.PlaybackState.Stopped;
        public WaveFormat OutputWaveFormat { get; private set; } = new(44100, 16, 2);
        public float Volume { get; set; }
        public event EventHandler<StoppedEventArgs>? PlaybackStopped
        {
            add { }
            remove { }
        }

        public void Init(IWaveProvider waveProvider) => OutputWaveFormat = waveProvider.WaveFormat;
        public void Play() => throw new InvalidOperationException("play failed");
        public void Pause() { }
        public void Stop() { }
        public void Dispose() => IsDisposed = true;
    }

    private sealed class FakeWavePlayerFactory
    {
        public List<FakeWavePlayer> CreatedPlayers { get; } = [];

        public IWavePlayer Create()
        {
            var player = new FakeWavePlayer();
            CreatedPlayers.Add(player);
            return player;
        }
    }
}
