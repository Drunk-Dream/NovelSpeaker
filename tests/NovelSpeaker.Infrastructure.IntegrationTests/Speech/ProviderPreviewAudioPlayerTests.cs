using Microsoft.Extensions.DependencyInjection;
using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Speech.Providers;
using NovelSpeaker.Infrastructure.DependencyInjection;
using NovelSpeaker.Infrastructure.FileSystem;
using Xunit;

namespace NovelSpeaker.Infrastructure.IntegrationTests.Speech;

public sealed class ProviderPreviewAudioPlayerTests
{
    [Fact]
    public async Task Preview_player_uses_isolated_temp_file_and_cleans_replaced_audio()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var directories = new AppDataDirectoryProvider(root);
        await directories.EnsureCreatedAsync(CancellationToken.None);
        try
        {
            var fake = new FakeAudioPlayer();
            var services = new ServiceCollection()
                .AddSingleton<IAppDataDirectoryProvider>(directories)
                .AddSingleton<IAppStoragePathResolver>(new AppStoragePathResolver(directories))
                .AddSingleton<IAudioPlayerFactory>(new FakeAudioPlayerFactory(fake))
                .AddNovelSpeakerSpeechAdapters()
                .BuildServiceProvider();
            await using (services.ConfigureAwait(false))
            {
                var preview = services.GetRequiredService<IProviderPreviewAudioPlayer>();
                await using var firstAudio = new MemoryStream([1, 2, 3]);
                var first = await preview.PlayAsync(firstAudio, "wav", CancellationToken.None);
                var firstPath = Assert.IsType<string>(fake.LastLoadedPath);
                Assert.True(first.IsSuccess);
                Assert.True(File.Exists(firstPath));

                await using var secondAudio = new MemoryStream([4, 5]);
                var second = await preview.PlayAsync(secondAudio, "mp3", CancellationToken.None);
                Assert.True(second.IsSuccess);
                Assert.False(File.Exists(firstPath));
                Assert.Equal(2, fake.PlayCount);
                Assert.True(File.Exists(fake.LastLoadedPath));

                string? playbackFailure = null;
                preview.PlaybackFailed += (_, args) => playbackFailure = args.Message;
                fake.RaisePlaybackFailure();
                Assert.Equal("试听播放中断，请检查音频设备。", playbackFailure);

                var lastPath = fake.LastLoadedPath;
                await preview.StopAsync(CancellationToken.None);
                Assert.False(File.Exists(lastPath));
                await preview.DisposeAsync();
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Preview_player_does_not_play_when_cancelled_during_load()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var directories = new AppDataDirectoryProvider(root);
        await directories.EnsureCreatedAsync(CancellationToken.None);
        try
        {
            using var cancellation = new CancellationTokenSource();
            var fake = new FakeAudioPlayer { OnLoad = cancellation.Cancel };
            var services = new ServiceCollection()
                .AddSingleton<IAppDataDirectoryProvider>(directories)
                .AddSingleton<IAppStoragePathResolver>(new AppStoragePathResolver(directories))
                .AddSingleton<IAudioPlayerFactory>(new FakeAudioPlayerFactory(fake))
                .AddNovelSpeakerSpeechAdapters()
                .BuildServiceProvider();
            await using (services.ConfigureAwait(false))
            {
                var preview = services.GetRequiredService<IProviderPreviewAudioPlayer>();
                await using var audio = new MemoryStream([1, 2, 3]);
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                    preview.PlayAsync(audio, "wav", cancellation.Token));
                Assert.Equal(0, fake.PlayCount);
                Assert.Empty(Directory.GetFiles(
                    Path.Combine(directories.CacheDirectoryPath, "ProviderPreviews")));
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class FakeAudioPlayerFactory(FakeAudioPlayer player) : IAudioPlayerFactory
    {
        public IAudioPlayer Create() => player;
    }

    private sealed class FakeAudioPlayer : IAudioPlayer
    {
        public PlaybackState State => default;
        public TimeSpan Position => TimeSpan.Zero;
        public TimeSpan Duration => TimeSpan.Zero;
        public double Volume { get; set; }
        public string? LastLoadedPath { get; private set; }
        public int PlayCount { get; private set; }
        public Action? OnLoad { get; init; }

        public event EventHandler? PlaybackCompleted
        {
            add { }
            remove { }
        }

        public event EventHandler<PlaybackErrorEventArgs>? PlaybackFailed;

        public Task LoadAsync(string filePath, CancellationToken cancellationToken)
        {
            Assert.True(File.Exists(filePath));
            LastLoadedPath = filePath;
            OnLoad?.Invoke();
            return Task.CompletedTask;
        }

        public void Play() => PlayCount++;
        public void Pause() { }
        public void Stop() { }
        public void Seek(TimeSpan position) { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void RaisePlaybackFailure() =>
            PlaybackFailed?.Invoke(this,
                new PlaybackErrorEventArgs(PlaybackErrorKind.OutputDevice, "设备错误"));
    }
}
