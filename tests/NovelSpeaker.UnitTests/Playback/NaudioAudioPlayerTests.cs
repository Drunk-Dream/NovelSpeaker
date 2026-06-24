using NAudio.Wave;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Infrastructure.Playback;
using PlaybackStatus = NovelSpeaker.Application.Playback.PlaybackState;
using Xunit;

namespace NovelSpeaker.UnitTests.Playback;

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

    private static string CreateShortWaveFile()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.wav");
        using var writer = new WaveFileWriter(filePath, new WaveFormat(8000, 16, 1));
        writer.WriteSample(0.1f);
        return filePath;
    }
}
