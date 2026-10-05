using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NAudio.Wave;
using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Application.Cache;
using NovelSpeaker.Application.Cache.ActiveCache;
using NovelSpeaker.Application.Cache.Audio;
using NovelSpeaker.Application.Cache.Export;
using NovelSpeaker.Application.DependencyInjection;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Application.Speech;
using NovelSpeaker.Application.Speech.Compilation;
using NovelSpeaker.Application.Speech.Execution;
using NovelSpeaker.Application.Speech.Providers;
using NovelSpeaker.Domain.Settings;
using NovelSpeaker.Domain.Speech.Providers;
using NovelSpeaker.Domain.Books;
using NovelSpeaker.Infrastructure.DependencyInjection;
using NovelSpeaker.Infrastructure.FileSystem;
using NovelSpeaker.Infrastructure.Persistence;
using NovelSpeaker.Infrastructure.Speech.Http;
using NovelSpeaker.Domain.Speech;
using NovelSpeaker.TestKit.Speech;
using Xunit;
using PlaybackState = NovelSpeaker.Application.Playback.PlaybackState;

namespace NovelSpeaker.Infrastructure.IntegrationTests;

public sealed class ProviderProductionPipelineTests
{
    [Theory]
    [InlineData("empty-once", PlaybackState.Playing)]
    [InlineData("empty", PlaybackState.Stopped)]
    public async Task Real_http_empty_audio_preserves_skip_and_consecutive_failure_recovery(string path, PlaybackState expectedState)
    {
        await using var fixture = await Fixture.CreateAsync(false, 0);
        await fixture.Services.GetRequiredService<SpeechProviderWorkspace>().SaveAsync(fixture.Provider with
        { Configuration = fixture.HttpConfiguration(path) }, false, CancellationToken.None);
        var playback = fixture.Services.GetRequiredService<IPlaybackSession>();
        await playback.StartAsync(new PlaybackStartRequest("book", 0, 0, null, 0), CancellationToken.None);
        Assert.Equal(expectedState, playback.CurrentSnapshot.State);
        Assert.Equal(1, playback.CurrentSnapshot.SegmentIndex);
        Assert.Equal(2, fixture.Server.GetRequestCount("/" + path));
        if (expectedState == PlaybackState.Playing)
            Assert.True(playback.CurrentSnapshot.HasLoadedAudio);
        else
        {
            Assert.Contains("已跳过并结束播放", playback.CurrentSnapshot.Message);
            Assert.False(playback.CurrentSnapshot.CanRetry);
            Assert.False(playback.CurrentSnapshot.HasLoadedAudio);
        }
    }

    [Fact]
    public async Task Foreground_preempts_running_prefetch_even_when_active_cache_is_queued()
    {
        await using var fixture = await Fixture.CreateAsync(false, 0, observeConcurrentRequests: true);
        var gate = fixture.Services.GetRequiredService<GatedTransport>();
        var admission = fixture.Services.GetRequiredService<AdmissionObserver>();
        var provider = await fixture.Services.GetRequiredService<ICurrentSpeechProvider>().GetSelectedProviderAsync(CancellationToken.None);
        var prefetch = fixture.Services.GetRequiredService<IAudioGenerationProvider>().GetAudioAsync(
            new AudioGenerationRequest("book", 0, 0, "Prefetch", provider!, 0, Guid.NewGuid())
            { ChapterId = "chapter", StableSegmentIdentity = StableSpeechSegmentIdentity.Body(0, 1) },
            AudioGenerationPriority.Prefetch, null, CancellationToken.None);
        await gate.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var active = fixture.Services.GetRequiredService<IActiveCacheCoordinator>();
        Assert.Equal(ActiveCacheStartStatus.Accepted,
            (await active.StartAsync(new StartActiveCacheRequest("book", [0], 0), CancellationToken.None)).Status);
        await admission.ActiveQueued.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var playback = fixture.Services.GetRequiredService<IPlaybackSession>();
        await playback.StartAsync(new PlaybackStartRequest("book", 0, 1, null, 0), CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(PlaybackState.Playing, playback.CurrentSnapshot.State);
        await gate.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(TtsErrorKind.Cancelled, (await prefetch).Failure!.Kind);
        await active.WaitForCurrentBatchAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(ActiveCacheBatchStatus.Completed, active.CurrentSnapshot!.Status);
    }

    [Fact]
    public async Task Paused_book_snapshot_distinguishes_opened_content_from_loaded_sentence_audio()
    {
        await using var fixture = await Fixture.CreateAsync(false, 0);
        var playback = fixture.Services.GetRequiredService<IPlaybackSession>();
        await playback.OpenPausedAsync(new OpenBookPlaybackRequest("book", 0, 0, 0), CancellationToken.None);
        Assert.Equal(PlaybackState.Paused, playback.CurrentSnapshot.State);
        Assert.False(playback.CurrentSnapshot.HasLoadedAudio);
        Assert.Equal(0, fixture.RequestCount);
        await playback.ResumeAsync(CancellationToken.None);
        await playback.PauseAsync(CancellationToken.None);
        Assert.Equal(PlaybackState.Paused, playback.CurrentSnapshot.State);
        Assert.True(playback.CurrentSnapshot.HasLoadedAudio);
        await fixture.Services.GetRequiredService<SpeechProviderWorkspace>().DeleteAsync(fixture.Provider.Id, CancellationToken.None);
        Assert.True(playback.CurrentSnapshot.HasLoadedAudio);
    }

    [Fact]
    public async Task Foreground_playback_retries_http_rate_limit_through_the_registered_runtime()
    {
        await using var fixture = await Fixture.CreateAsync(false, 0);
        await fixture.Services.GetRequiredService<SpeechProviderWorkspace>().SaveAsync(fixture.Provider with
        { Configuration = fixture.HttpConfiguration("rate-limited") }, false, CancellationToken.None);
        var playback = fixture.Services.GetRequiredService<IPlaybackSession>();
        await playback.StartAsync(new PlaybackStartRequest("book", 0, 0, null, 0), CancellationToken.None);
        Assert.Equal(PlaybackState.Playing, playback.CurrentSnapshot.State);
        Assert.Equal(2, fixture.Server.GetRequestCount("/rate-limited"));
        Assert.True(File.Exists(fixture.Player.LoadedFile));
    }

    [Fact]
    public async Task Non_http_transient_synthesis_failures_are_retried_at_the_shared_audio_boundary()
    {
        await using var fixture = await Fixture.CreateAsync(true, 0);
        fixture.Edge.Failures.Enqueue(new ProviderSynthesisFailure(ProviderSynthesisFailureKind.Network, "连接失败。"));
        fixture.Edge.Failures.Enqueue(new ProviderSynthesisFailure(ProviderSynthesisFailureKind.Timeout, "连接超时。"));

        var playback = fixture.Services.GetRequiredService<IPlaybackSession>();
        await playback.StartAsync(new PlaybackStartRequest("book", 0, 0, null, 0), CancellationToken.None);

        Assert.Equal(PlaybackState.Playing, playback.CurrentSnapshot.State);
        Assert.Equal(3, fixture.Edge.Calls.Count);
    }

    [Fact]
    public async Task Rate_limited_synthesis_stops_after_bounded_retries()
    {
        await using var fixture = await Fixture.CreateAsync(true, 0);
        fixture.Edge.DefaultFailure = new ProviderSynthesisFailure(
            ProviderSynthesisFailureKind.RateLimited, "请求过于频繁。", TimeSpan.Zero);
        var provider = await fixture.Services.GetRequiredService<ICurrentSpeechProvider>()
            .GetSelectedProviderAsync(CancellationToken.None);
        var audio = fixture.Services.GetRequiredService<IAudioGenerationProvider>();

        var result = await audio.GetAudioAsync(
            new AudioGenerationRequest("book", 0, 0, "Rate limited", provider!, 0, Guid.NewGuid())
            { ChapterId = "chapter", StableSegmentIdentity = StableSpeechSegmentIdentity.Body(0, 1) },
            AudioGenerationPriority.Current,
            null,
            CancellationToken.None);

        Assert.Equal(TtsErrorKind.RateLimited, result.Failure?.Kind);
        Assert.Equal(3, fixture.Edge.Calls.Count);
    }

    [Fact]
    public async Task Foreground_playback_retries_when_the_prefetch_owner_of_shared_audio_is_cancelled()
    {
        await using var fixture = await Fixture.CreateAsync(true, 0);
        fixture.Edge.BlockNext = true;
        var provider = await fixture.Services.GetRequiredService<ICurrentSpeechProvider>().GetSelectedProviderAsync(CancellationToken.None);
        var audio = fixture.Services.GetRequiredService<IAudioGenerationProvider>();
        var request = new AudioGenerationRequest("book", 0, 0, "First.", provider!, 0, Guid.NewGuid())
        { ChapterId = "chapter", StableSegmentIdentity = StableSpeechSegmentIdentity.Body(0, 6) };
        using var prefetchCancellation = new CancellationTokenSource();
        var prefetch = audio.GetAudioAsync(request, AudioGenerationPriority.Prefetch, null, prefetchCancellation.Token);
        await fixture.Edge.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var foreground = audio.GetAudioAsync(request, AudioGenerationPriority.Current, null, CancellationToken.None);
        prefetchCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => prefetch);
        var result = await foreground.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(result.IsSuccess);
        Assert.True(File.Exists(result.FilePath));
        Assert.Equal(2, fixture.RequestCount);
    }

    [Fact]
    public async Task Missing_registered_http_runtime_stops_generation_without_falling_back_to_edge()
    {
        await using var fixture = await Fixture.CreateAsync(false, 0, omitHttpRuntime: true);
        var playback = fixture.Services.GetRequiredService<IPlaybackSession>();
        await playback.StartAsync(new PlaybackStartRequest("book", 0, 0, null, 0), CancellationToken.None);
        Assert.Equal(PlaybackState.Stopped, playback.CurrentSnapshot.State);
        Assert.False(playback.CurrentSnapshot.HasAvailableProvider);
        Assert.Equal(0, fixture.RequestCount);
        var active = fixture.Services.GetRequiredService<IActiveCacheCoordinator>();
        Assert.Equal(ActiveCacheStartStatus.SelectedProviderUnavailable,
            (await active.StartAsync(new StartActiveCacheRequest("book", [0], 0), CancellationToken.None)).Status);
        Assert.Equal(0, fixture.RequestCount);
    }

    [Fact]
    public async Task Active_cache_freezes_saved_voice_rate_and_text_inputs_even_when_edge_is_hidden_mid_batch()
    {
        await using var fixture = await Fixture.CreateAsync(true, 0);
        fixture.Edge.BlockNext = true;
        var active = fixture.Services.GetRequiredService<IActiveCacheCoordinator>();
        await active.StartAsync(new StartActiveCacheRequest("book", [0], 0), CancellationToken.None);
        await fixture.Edge.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var workspace = fixture.Services.GetRequiredService<SpeechProviderWorkspace>();
        await workspace.SaveAsync(fixture.Provider with
        {
            Configuration = new EdgeSpeechProviderConfiguration(new EdgeVoice("new-voice", "New", "en-US", "Female"))
        }, false, CancellationToken.None);
        await fixture.Services.GetRequiredService<IAppSettingsService>().UpdateAsync(
            new AppSettingsUpdate { DefaultSpeakSpeed = 50, ReadChapterTitle = true }, CancellationToken.None);
        await workspace.SetEdgeEnabledAsync(false, CancellationToken.None);
        fixture.Edge.Release.TrySetResult();
        await active.WaitForCurrentBatchAsync(CancellationToken.None);
        Assert.Equal(ActiveCacheBatchStatus.Completed, active.CurrentSnapshot!.Status);
        Assert.Equal(["First.", "Second."], fixture.Edge.Calls.Select(call => call.Text));
        Assert.All(fixture.Edge.Calls, call => { Assert.Equal("voice", call.Voice); Assert.Equal(-100, call.Rate); });
        Assert.Equal(ActiveCacheStartStatus.SelectedProviderUnavailable,
            (await active.StartAsync(new StartActiveCacheRequest("book", [0], 50), CancellationToken.None)).Status);
    }

    [Fact]
    public async Task Pending_prefetch_uses_latest_saved_voice_and_speed_when_each_request_starts()
    {
        await using var fixture = await Fixture.CreateAsync(true, 0);
        fixture.Edge.BlockNext = true;
        var provider = await fixture.Services.GetRequiredService<ICurrentSpeechProvider>().GetSelectedProviderAsync(CancellationToken.None);
        var prefetch = fixture.Services.GetRequiredService<IPlaybackPrefetchController>();
        var sessionId = Guid.NewGuid();
        var requests = Enumerable.Range(0, 2).Select(index => new AudioGenerationRequest("book", 0, index,
            $"Prefetch {index}", provider!, 0, sessionId)
        { ChapterId = "chapter", StableSegmentIdentity = StableSpeechSegmentIdentity.Body(index, 1) }).ToArray();
        await prefetch.SubmitAsync(new PlaybackPrefetchWindow(sessionId, requests), CancellationToken.None);
        await fixture.Edge.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await fixture.Services.GetRequiredService<SpeechProviderWorkspace>().SaveAsync(fixture.Provider with
        { Configuration = new EdgeSpeechProviderConfiguration(new EdgeVoice("new-voice", "New", "en-US", "Female")) }, false, CancellationToken.None);
        await fixture.Services.GetRequiredService<IAppSettingsService>().UpdateAsync(
            new AppSettingsUpdate { DefaultSpeakSpeed = 50 }, CancellationToken.None);
        fixture.Edge.Release.TrySetResult();
        await fixture.Edge.SecondStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await prefetch.CancelAsync(sessionId, CancellationToken.None);
        Assert.Equal(("voice", "Prefetch 0", -100), fixture.Edge.Calls[0]);
        Assert.Equal(("new-voice", "Prefetch 1", 0), fixture.Edge.Calls[1]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Registered_provider_runs_playback_prefetch_active_cache_coverage_and_verified_export(bool edge)
    {
        await using var fixture = await Fixture.CreateAsync(edge, prefetchCount: 1);
        var playback = fixture.Services.GetRequiredService<IPlaybackSession>();
        await playback.StartAsync(new PlaybackStartRequest("book", 0, 0, null, 0), CancellationToken.None);
        Assert.Equal(PlaybackState.Playing, playback.CurrentSnapshot.State);
        Assert.Equal(0, playback.CurrentSnapshot.SpeakSpeed);
        Assert.True(File.Exists(fixture.Player.LoadedFile));
        await fixture.WaitForCoverageAsync();
        // The original profile has exercised playback + prefetch. Quiesce that window
        // before measuring the new profile's active-cache transport requests.
        await fixture.Services.GetRequiredService<IAppSettingsService>().UpdateAsync(
            new AppSettingsUpdate { DefaultSpeakSpeed = 1, PrefetchCount = 0 }, CancellationToken.None);
        await WaitForSnapshotAsync(playback, snapshot => snapshot.SpeakSpeed == 1);
        var active = fixture.Services.GetRequiredService<IActiveCacheCoordinator>();
        var start = await active.StartAsync(new StartActiveCacheRequest("book", [0], 1), CancellationToken.None);
        Assert.Equal(ActiveCacheStartStatus.Accepted, start.Status);
        await active.WaitForCurrentBatchAsync(CancellationToken.None);
        Assert.Equal(ActiveCacheBatchStatus.Completed, active.CurrentSnapshot!.Status);
        var result = await fixture.ExportAsync();
        Assert.Equal(ExportChaptersStatus.Succeeded, result.Status);
        using var reader = new AudioFileReader(Assert.Single(result.Files).FilePath);
        Assert.True(reader.TotalTime > TimeSpan.Zero);
        Assert.Equal(4, fixture.RequestCount);
        if (edge) Assert.Equal([-100, -100, -98, -98], fixture.Edge.Rates);

        var settings = fixture.Services.GetRequiredService<IAppSettingsService>();
        await settings.UpdateAsync(new AppSettingsUpdate { ClearCurrentProvider = true }, CancellationToken.None);
        Assert.Equal(PlaybackState.Playing, playback.CurrentSnapshot.State);
        var count = fixture.RequestCount;
        var missing = await active.StartAsync(new StartActiveCacheRequest("book", [0], 0), CancellationToken.None);
        Assert.Equal(ActiveCacheStartStatus.SelectedProviderUnavailable, missing.Status);
        Assert.Equal(ExportChaptersStatus.SelectedProviderUnavailable, (await fixture.ExportAsync()).Status);
        Assert.Equal(count, fixture.RequestCount);
    }

    [Theory]
    [InlineData("switch")]
    [InlineData("edit")]
    [InlineData("delete")]
    [InlineData("unconfigured")]
    [InlineData("hide")]
    [InlineData("none")]
    public async Task Saved_selection_changes_preserve_current_audio_and_next_sentence_reads_latest_state(string change)
    {
        await using var fixture = await Fixture.CreateAsync(change is "hide" or "unconfigured", prefetchCount: 0);
        var playback = fixture.Services.GetRequiredService<IPlaybackSession>();
        var settings = fixture.Services.GetRequiredService<IAppSettingsService>();
        var store = fixture.Services.GetRequiredService<IProviderStore>();
        var workspace = fixture.Services.GetRequiredService<SpeechProviderWorkspace>();
        await playback.StartAsync(new PlaybackStartRequest("book", 0, 0, null, 0), CancellationToken.None);
        var loaded = fixture.Player.LoadedFile;
        var expectedId = fixture.Provider.Id;
        if (change == "switch")
        {
            var other = fixture.Provider with { Id = ProviderId.New(), Name = "Second", Configuration = fixture.HttpConfiguration("audio-json") };
            await store.SaveAsync(other, CancellationToken.None);
            expectedId = other.Id;
            await playback.ChangeProviderAsync(other.Id, CancellationToken.None);
        }
        else if (change == "edit")
            await workspace.SaveAsync(fixture.Provider with { Configuration = fixture.HttpConfiguration("audio-json") }, false, CancellationToken.None);
        else if (change == "delete")
            await workspace.DeleteAsync(fixture.Provider.Id, CancellationToken.None);
        else if (change == "unconfigured")
            await store.SaveAsync(fixture.Provider with { Configuration = new EdgeSpeechProviderConfiguration(null) }, CancellationToken.None);
        else if (change == "none")
            await settings.UpdateAsync(new AppSettingsUpdate { ClearCurrentProvider = true }, CancellationToken.None);
        else
            await workspace.SetEdgeEnabledAsync(false, CancellationToken.None);
        Assert.Equal(PlaybackState.Playing, playback.CurrentSnapshot.State);
        Assert.Equal(loaded, fixture.Player.LoadedFile);

        var completed = WaitForSnapshotAsync(playback, snapshot => snapshot.SegmentIndex == 1 && snapshot.State != PlaybackState.Buffering);
        fixture.Player.Complete();
        await completed;
        if (change is "switch" or "edit")
        {
            Assert.Equal(PlaybackState.Playing, playback.CurrentSnapshot.State);
            Assert.Equal(expectedId, playback.CurrentSnapshot.ProviderId);
            Assert.Equal(1, fixture.Server.GetRequestCount("/audio"));
            Assert.Equal(1, fixture.Server.GetRequestCount("/audio-json"));
            Assert.NotEqual(loaded, fixture.Player.LoadedFile);
        }
        else
        {
            Assert.Equal(PlaybackState.Stopped, playback.CurrentSnapshot.State);
            Assert.False(playback.CurrentSnapshot.HasAvailableProvider);
            Assert.Equal(1, fixture.RequestCount);
            var progress = await fixture.Services.GetRequiredService<IReadingProgressStore>().GetAsync("book", CancellationToken.None);
            Assert.Equal(0, progress!.SegmentIndex);
        }
    }

    private static async Task WaitForSnapshotAsync(IPlaybackSnapshotSource source, Func<PlaybackSnapshot, bool> predicate)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Changed(object? sender, PlaybackSnapshot snapshot) { if (predicate(snapshot)) completion.TrySetResult(); }
        source.SnapshotChanged += Changed;
        try
        {
            if (!predicate(source.CurrentSnapshot)) await completion.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally { source.SnapshotChanged -= Changed; }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        public LocalHttpTtsTestServer Server { get; } = new();
        public Player Player { get; } = new();
        public EdgeTransport Edge { get; } = new();
        public ServiceProvider Services { get; private set; } = null!;
        public SpeechProviderInstance Provider { get; private set; } = null!;
        public int RequestCount => Server.GetRequestCount("/audio") + Server.GetRequestCount("/audio-json") + Edge.Rates.Count;
        public HttpSpeechProviderConfiguration HttpConfiguration(string path) => new(new Uri(Server.BaseUri, path).ToString(),
            "GET", new Dictionary<string, string>(), null, null);

        public static async Task<Fixture> CreateAsync(bool edge, int prefetchCount, bool omitHttpRuntime = false, bool observeConcurrentRequests = false)
        {
            var fixture = new Fixture();
            var directories = new AppDataDirectoryProvider(fixture._root);
            var now = DateTimeOffset.UtcNow;
            fixture.Provider = new SpeechProviderInstance(ProviderId.New(), edge ? "Microsoft Edge" : "HTTP", 0,
                edge ? new EdgeSpeechProviderConfiguration(new EdgeVoice("voice", "Voice", "en-US", "Female")) : fixture.HttpConfiguration("audio"), now, now);
            var settings = AppSettings.Default with
            {
                CurrentProviderId = fixture.Provider.Id,
                DefaultSpeakSpeed = 0,
                PrefetchCount = prefetchCount,
                ReadChapterTitle = false,
                EnabledExperimentalFeatureIds = edge ? [ExperimentalFeaturesService.MicrosoftEdgeTts] : []
            };
            var services = new ServiceCollection().AddLogging()
                .AddSingleton<IAppDataDirectoryProvider>(directories)
                .AddSingleton<IAudioPlayer>(fixture.Player)
                .AddSingleton<IEdgeSpeechTransport>(fixture.Edge)
                .AddNovelSpeakerApplication(settings).AddNovelSpeakerInfrastructure();
            services.RemoveAll<ISqliteConnectionFactory>();
            services.AddSingleton<ISqliteConnectionFactory>(provider =>
                new SqliteConnectionFactory(provider.GetRequiredService<IAppDataDirectoryProvider>(), null, pooling: false));
            if (observeConcurrentRequests)
            {
                services.AddSingleton<ProviderRequestLimiter>().AddSingleton<AdmissionObserver>()
                    .AddSingleton<IProviderRequestLimiter>(provider => provider.GetRequiredService<AdmissionObserver>())
                    .AddSingleton<HttpTtsClient>().AddSingleton<GatedTransport>()
                    .AddSingleton<ITtsHttpTransport>(provider => provider.GetRequiredService<GatedTransport>());
            }
            if (omitHttpRuntime)
            {
                var registration = services.Single(descriptor => descriptor.ImplementationType == typeof(HttpProviderRuntime));
                services.Remove(registration);
            }
            fixture.Services = services.BuildServiceProvider();
            await fixture.Services.GetRequiredService<IDatabaseInitializer>().InitializeAsync(CancellationToken.None);
            await fixture.Services.GetRequiredService<IProviderStore>().SaveAsync(fixture.Provider, CancellationToken.None);
            await fixture.Services.GetRequiredService<IAppSettingsService>().UpdateAsync(
                new AppSettingsUpdate { CurrentProviderId = fixture.Provider.Id }, CancellationToken.None);
            var text = "First.\nSecond.";
            var contentPath = Path.Combine(directories.BooksDirectoryPath, "content.txt");
            await File.WriteAllTextAsync(contentPath, text);
            await using var connection = await fixture.Services.GetRequiredService<ISqliteConnectionFactory>().OpenConnectionAsync(CancellationToken.None);
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO Books (Id, Title, Author, Description, ImportedAt, UpdatedAt) VALUES
                ('book', 'Book', NULL, NULL, '2026-01-01', '2026-01-01');
                INSERT INTO BookSources (Id, BookId, SourceType, Title, Author, Description, CreatedAt, UpdatedAt) VALUES ('local:' || 'book', 'book', 1, 'Book', NULL, NULL, '2026-01-01', '2026-01-01');
                INSERT INTO LocalBookSources (SourceId, OriginalFileName, StoredContentPath, SourceHash, Encoding, ImportedAt, LastImportedAt) VALUES ('local:' || 'book', 'book.txt', 'Books/content.txt', 'provider-pipeline', 'utf-8', '2026-01-01', '2026-01-01');
                UPDATE Books SET ActiveSourceId = 'local:' || 'book' WHERE Id = 'book';
                INSERT INTO Chapters (Id, SourceId, ChapterIndex, SortOrder, Title) VALUES
                ('chapter', 'local:' || 'book', 0, 0, 'Chapter');
                INSERT INTO LocalChapterContents (ChapterId, StartOffset, Length) VALUES
                ('chapter', 0, 14);
                """;
            await command.ExecuteNonQueryAsync();
            return fixture;
        }

        public Task<ExportChaptersResult> ExportAsync() => Services.GetRequiredService<IExportChaptersService>()
            .ExportAsync(new ExportChaptersRequest("book", [0], Path.Combine(_root, "export")), CancellationToken.None);

        public async Task WaitForCoverageAsync()
        {
            var invalidation = Services.GetRequiredService<ICacheInvalidationCoordinator>();
            var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Changed(object? sender, CacheInvalidationBatch batch) => completed.TrySetResult();
            invalidation.BatchPublished += Changed;
            try
            {
                while (true)
                {
                    var coverage = await Services.GetRequiredService<ICacheCoverageQuery>().GetAsync("book", [0], CancellationToken.None);
                    var status = Assert.Single(coverage);
                    if (status.TotalSegmentCount is > 0 && status.CachedSegmentCount == status.TotalSegmentCount) return;
                    await invalidation.FlushPendingAsync(CancellationToken.None);
                    await completed.Task.WaitAsync(TimeSpan.FromSeconds(10));
                    completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
                }
            }
            finally { invalidation.BatchPublished -= Changed; }
        }

        public async ValueTask DisposeAsync()
        {
            await Services.DisposeAsync();
            await Server.DisposeAsync();
            Directory.Delete(_root, true);
        }
    }

    private sealed class GatedTransport(HttpTtsClient inner) : ITtsHttpTransport
    {
        private int _requests;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<TtsTransportResult> SendAsync(ParsedTtsRequest request, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _requests) == 1)
            {
                Started.TrySetResult();
                try { await _release.Task.WaitAsync(cancellationToken); }
                catch (OperationCanceledException) { Cancelled.TrySetResult(); throw; }
            }
            return await inner.SendAsync(request, cancellationToken);
        }
    }

    private sealed class AdmissionObserver(ProviderRequestLimiter inner) : IProviderRequestLimiter
    {
        public TaskCompletionSource ActiveQueued { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<ITtsAdmissionLease> AcquireAsync(ProviderId providerId, ProviderRequestRateLimit? rateLimit,
            TtsAdmissionPriority priority, CancellationToken cancellationToken)
        {
            var pending = inner.AcquireAsync(providerId, rateLimit, priority, cancellationToken);
            if (priority == TtsAdmissionPriority.ActiveCache && !pending.IsCompleted) ActiveQueued.TrySetResult();
            return pending;
        }
        public void ApplyRetryAfter(ProviderId providerId, TimeSpan retryAfter) => inner.ApplyRetryAfter(providerId, retryAfter);
    }

    private sealed class EdgeTransport : IEdgeSpeechTransport
    {
        public List<int> Rates { get; } = [];
        public List<(string Voice, string Text, int Rate)> Calls { get; } = [];
        public Queue<ProviderSynthesisFailure> Failures { get; } = new();
        public ProviderSynthesisFailure? DefaultFailure { get; set; }
        public bool BlockNext { get; set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SecondStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<IReadOnlyList<EdgeVoice>> GetVoicesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<EdgeVoice>>([]);
        public async Task<ProviderSynthesisResult> SynthesizeAsync(EdgeVoice voice, string text, int ratePercent, CancellationToken cancellationToken)
        {
            Rates.Add(ratePercent);
            Calls.Add((voice.VoiceId, text, ratePercent));
            if (Calls.Count == 2) SecondStarted.TrySetResult();
            if (BlockNext)
            {
                BlockNext = false;
                Started.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
            var failure = Failures.Count > 0 ? Failures.Dequeue() : DefaultFailure;
            if (failure is not null)
                return new ProviderSynthesisResult(null, null, failure);
            return new ProviderSynthesisResult(File.OpenRead(Path.Combine(AppContext.BaseDirectory, "TestAssets", "Audio", "demo-tone.mp3")), "audio/mpeg", null, "mp3");
        }
    }

    private sealed class Player : IAudioPlayer
    {
        public string? LoadedFile { get; private set; }
        public PlaybackState State { get; private set; }
        public TimeSpan Position { get; private set; }
        public TimeSpan Duration => TimeSpan.FromSeconds(1);
        public double Volume { get; set; }
        public event EventHandler? PlaybackCompleted;
        public event EventHandler<PlaybackErrorEventArgs>? PlaybackFailed { add { } remove { } }
        public Task LoadAsync(string filePath, CancellationToken cancellationToken) { LoadedFile = filePath; State = PlaybackState.Stopped; return Task.CompletedTask; }
        public void Play() => State = PlaybackState.Playing;
        public void Pause() => State = PlaybackState.Paused;
        public void Stop() => State = PlaybackState.Stopped;
        public void Seek(TimeSpan position) => Position = position;
        public void Complete() { Position = Duration; State = PlaybackState.Stopped; PlaybackCompleted?.Invoke(this, EventArgs.Empty); }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
