using NovelSpeaker.Application.Cache;
using System.IO;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Cache.ActiveCache;
using NovelSpeaker.Application.Cache.Audio;
using NovelSpeaker.Application.DependencyInjection;
using NovelSpeaker.Application.Observability;
using NovelSpeaker.Application.Speech.Providers;
using NovelSpeaker.Domain.Books;
using NovelSpeaker.Infrastructure.DependencyInjection;
using NovelSpeaker.Infrastructure.FileSystem;
using NovelSpeaker.TestKit.Speech;
using Xunit;

namespace NovelSpeaker.App.WpfTests.Bootstrap;

[Collection("WpfDispatcher")]
public sealed class ActiveCacheShutdownHostTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Process_shutdown_stops_the_registered_batch_and_bounds_container_disposal(bool cooperative)
    {
        await WpfTestHost.RunInStaAsync(async () =>
        {
            var root = Path.Combine(Path.GetTempPath(), "NovelSpeaker-shutdown-" + Guid.NewGuid().ToString("N"));
            var services = new ServiceCollection();
            var released = new DisposalSignal();
            services.AddSingleton(_ => released);
            services.AddLogging();
            services.AddSingleton<IAppDataDirectoryProvider>(new AppDataDirectoryProvider(root));
            services.AddNovelSpeakerApplication();
            services.AddNovelSpeakerInfrastructure();
            services.AddNovelSpeakerDesktop();
            var observations = new ShutdownObservations();
            services.AddSingleton<IObservabilityConsumer>(observations);
            var audio = new GateAudio(cooperative);
            services.AddSingleton<IBookPlaybackContentService>(new Content());
            services.AddSingleton<ICurrentSpeechProvider>(new Provider());
            services.AddSingleton<IAudioGenerationProvider>(audio);
            var provider = WpfStartupRuntime.BuildValidatedServiceProvider(services);
            provider.GetRequiredService<DisposalSignal>();
            await provider.GetRequiredService<IDatabaseInitializer>().InitializeAsync(CancellationToken.None);
            var active = provider.GetRequiredService<IActiveCacheCoordinator>();
            Assert.Same(active, provider.GetRequiredService<IActiveCacheLifetime>());
            var runtime = new WpfStartupRuntime(Dispatcher.CurrentDispatcher, _ => { },
                TimeSpan.FromMilliseconds(300), provider);
            await using var startup = new StartupCoordinator(runtime);
            Task? batch = null;
            try
            {
                await active.StartAsync(new("synthetic-book", [0], 10), CancellationToken.None);
                await audio.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
                batch = active.WaitForCurrentBatchAsync(CancellationToken.None);
                var shutdown = startup.ShutdownAsync();
                Assert.True(audio.Token.IsCancellationRequested);
                await shutdown.WaitAsync(TimeSpan.FromSeconds(3));
                Assert.True(startup.ProcessToken.IsCancellationRequested);
                Assert.Equal(cooperative ? ActiveCacheBatchStatus.Cancelled : ActiveCacheBatchStatus.Cancelling,
                    active.CurrentSnapshot!.Status);
                Assert.Equal(0, active.CurrentSnapshot.CompletedSegmentCount);
                Assert.Equal(cooperative, batch.IsCompleted);
                Assert.Equal(cooperative ? OperationOutcome.Succeeded : OperationOutcome.Failed,
                    observations.Result!.Value.Outcome);
                if (!cooperative) Assert.Equal("background-shutdown-incomplete", observations.Result.Value.FailureCode);
            }
            finally
            {
                audio.Release.TrySetResult(new("synthetic.mp3", false, null));
                if (batch is not null) await batch.WaitAsync(TimeSpan.FromSeconds(5));
                await runtime.DisposeAsync();
                await released.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            }
        });
    }

    private sealed class DisposalSignal : IDisposable
    {
        public TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Dispose() => Disposed.TrySetResult();
    }

    private sealed class ShutdownObservations : IObservabilityConsumer
    {
        public OperationResult? Result { get; private set; }
        public void OnOperationStarted(OperationStarted operation) { }
        public void OnDiagnosticEvent(DiagnosticEvent diagnosticEvent) { }
        public void OnOperationCompleted(OperationCompleted operation)
        {
            if (operation.Operation == OperationCatalog.AppShutdown) Result = operation.Result;
        }
    }

    private sealed class GateAudio(bool cooperative) : IAudioGenerationProvider
    {
        public CancellationToken Token { get; private set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<AudioGenerationResult> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<AudioGenerationResult> GetAudioAsync(AudioGenerationRequest request, AudioGenerationPriority priority,
            Action<AudioGenerationProgress>? progressCallback, CancellationToken cancellationToken)
        {
            Token = cancellationToken;
            Started.TrySetResult();
            return cooperative ? Release.Task.WaitAsync(cancellationToken) : Release.Task;
        }
        public Task InvalidateAsync(AudioGenerationRequest request, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class Provider : TestCurrentSpeechProvider
    {
        public override Task<ResolvedSpeechProvider?> GetSelectedProviderAsync(CancellationToken cancellationToken) =>
            Task.FromResult<ResolvedSpeechProvider?>(TestSpeechProviders.Resolve(TestSpeechProviders.Item(1, "Synthetic")));
    }

    private sealed class Content : IBookPlaybackContentService
    {
        public Task<ChapterContentReader> FreezeChapterReaderAsync(CancellationToken cancellationToken) =>
            Task.FromResult<ChapterContentReader>(GetChapterAsync);

        public Task<PlaybackBookContent?> GetBookAsync(string bookId, CancellationToken cancellationToken) =>
            Task.FromResult<PlaybackBookContent?>(new(bookId, "Synthetic", [PlaybackChapterContent.Unloaded(0, "Chapter")]));
        public Task<PlaybackChapterContent?> GetChapterAsync(string bookId, int chapterIndex, CancellationToken cancellationToken) =>
            Task.FromResult<PlaybackChapterContent?>(PlaybackChapterContent.FromLoaded(0, "Chapter", [new SpeechSegment(0, 0, 3, "abc", "abc")]));
    }
}
