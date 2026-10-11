using NovelSpeaker.Application.Settings;
using NovelSpeaker.Application.Speech.Providers;
using NovelSpeaker.Domain.Settings;
using NovelSpeaker.Domain.Speech.Providers;
using NovelSpeaker.TestKit.Common;
using Xunit;

namespace NovelSpeaker.Application.UnitTests;

public sealed class EdgeProviderTests
{
    private static readonly EdgeVoice Voice = new("voice-A", "Voice A", "zh-CN", "Female");

    [Fact]
    public async Task Feature_lifecycle_preserves_instance_configuration_order_and_never_restores_selection()
    {
        var store = new ProviderStore();
        using var settings = new AppSettingsService(new SettingsStore(), AppSettings.Default with
        {
            EnabledExperimentalFeatureIds = ["unknown-future-feature"]
        });
        var features = new ExperimentalFeaturesService(settings);
        var workspace = new SpeechProviderWorkspace(store, TimeProvider.System, settings);
        var runtime = new EdgeProviderRuntime(new Transport());
        var resolver = new ProviderRuntimeResolver(store, edgeRuntime: runtime, features: features);
        Assert.False(features.IsEnabled(ExperimentalFeaturesService.MicrosoftEdgeTts));
        Assert.Empty(store.Items);

        await workspace.SetEdgeEnabledAsync(true, CancellationToken.None);
        var edge = Assert.Single(store.Items);
        Assert.Equal("Microsoft Edge", edge.Name);
        Assert.False((await resolver.ResolveAsync(edge.Id, CancellationToken.None)).IsAvailable);
        Assert.Null(((EdgeSpeechProviderConfiguration)edge.Configuration).Voice);
        var draft = edge with { Configuration = new EdgeSpeechProviderConfiguration(Voice) };
        var preview = await runtime.SynthesizeAsync(draft, new ProviderSynthesisRequest("test", 50), CancellationToken.None);
        await preview.Audio!.DisposeAsync();
        Assert.Null(((EdgeSpeechProviderConfiguration)store.Items[0].Configuration).Voice);
        var saved = await workspace.SaveAsync(draft, false, CancellationToken.None);
        Assert.True((await resolver.ResolveAsync(edge.Id, CancellationToken.None)).IsAvailable);
        await settings.UpdateAsync(new AppSettingsUpdate { CurrentProviderId = edge.Id }, CancellationToken.None);
        await workspace.SetEdgeEnabledAsync(false, CancellationToken.None);
        Assert.False(workspace.IsVisible(saved));
        Assert.Null(settings.Current.CurrentProviderId);
        Assert.Equal(ProviderRuntimeUnavailableReason.ProviderHidden,
            (await resolver.ResolveAsync(edge.Id, CancellationToken.None)).UnavailableReason);
        await workspace.SetEdgeEnabledAsync(true, CancellationToken.None);
        Assert.Equal(saved, Assert.Single(store.Items));
        Assert.True(workspace.IsVisible(saved));
        Assert.Null(settings.Current.CurrentProviderId);
        Assert.Contains("unknown-future-feature", settings.Current.EnabledExperimentalFeatureIds!);
        await Assert.ThrowsAsync<InvalidOperationException>(() => workspace.SaveAsync(draft, true, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => workspace.CopyAsync(edge.Id, CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => workspace.DeleteAsync(edge.Id, CancellationToken.None));
        Assert.Equal(ProviderExportStatus.ProviderUnavailable,
            (await workspace.ExportAsync(edge.Id, true, CancellationToken.None)).Status);
    }

    [Fact]
    public async Task Failed_or_cancelled_disable_preserves_feature_and_current_selection()
    {
        foreach (var cancel in new[] { false, true })
        {
            var store = new ProviderStore();
            var persisted = new SettingsStore();
            using var settings = new AppSettingsService(persisted, AppSettings.Default);
            var workspace = new SpeechProviderWorkspace(store, TimeProvider.System, settings);
            await workspace.SetEdgeEnabledAsync(true, CancellationToken.None);
            var edge = Assert.Single(store.Items);
            await settings.UpdateAsync(new AppSettingsUpdate { CurrentProviderId = edge.Id }, CancellationToken.None);
            using var cancellation = new CancellationTokenSource();
            persisted.CancelSave = cancel ? cancellation : null;
            persisted.FailSave = !cancel;
            if (cancel)
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => workspace.SetEdgeEnabledAsync(false, cancellation.Token));
            else
                await Assert.ThrowsAsync<IOException>(() => workspace.SetEdgeEnabledAsync(false, cancellation.Token));
            Assert.Equal(edge.Id, settings.Current.CurrentProviderId);
            Assert.True(new ExperimentalFeaturesService(settings).IsEnabled(ExperimentalFeaturesService.MicrosoftEdgeTts));
            Assert.Equal(edge.Id, persisted.Current!.CurrentProviderId);
            Assert.Contains(ExperimentalFeaturesService.MicrosoftEdgeTts, persisted.Current.EnabledExperimentalFeatureIds!);
        }
    }

    [Fact]
    public async Task Runtime_maps_public_speed_and_preserves_audio_and_failures()
    {
        foreach (var (speed, rate) in new[] { (0, -100), (50, 0), (100, 100) })
        {
            var transport = new Transport();
            var runtime = new EdgeProviderRuntime(transport);
            var provider = new SpeechProviderInstance(ProviderId.New(), "Microsoft Edge", 0,
                new EdgeSpeechProviderConfiguration(Voice), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
            var result = await runtime.SynthesizeAsync(provider, new ProviderSynthesisRequest("test", speed), CancellationToken.None);
            Assert.Equal(rate, transport.Rate);
            Assert.Equal(Voice, transport.LastVoice);
            Assert.True(result.IsSuccess);
            await result.Audio!.DisposeAsync();
            transport.Failure = new ProviderSynthesisFailure(ProviderSynthesisFailureKind.Network, "network");
            result = await runtime.SynthesizeAsync(provider, new ProviderSynthesisRequest("test", speed), CancellationToken.None);
            Assert.Equal(transport.Failure, result.Failure);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runtime.SynthesizeAsync(provider,
                new ProviderSynthesisRequest("test", speed), cancellation.Token));
        }
    }

    [Fact]
    public async Task Catalog_is_process_only_refresh_bypasses_cache_and_failure_retains_snapshot_and_saved_voice()
    {
        var transport = new Transport { Voices = [Voice] };
        var clock = new ManualTimeProvider();
        var catalog = new EdgeVoiceCatalog(transport, clock);
        var snapshot = await catalog.GetAsync(false, CancellationToken.None);
        transport.Voices = [Voice with { VoiceId = "voice-B" }];
        Assert.Same(snapshot, await catalog.GetAsync(false, CancellationToken.None));
        var refreshed = await catalog.GetAsync(true, CancellationToken.None);
        Assert.Equal("voice-B", Assert.Single(refreshed).VoiceId);
        transport.ThrowOnCatalog = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => catalog.GetAsync(true, CancellationToken.None));
        Assert.Same(refreshed, catalog.Current);
        transport.ThrowOnCatalog = false;
        transport.Voices = [Voice with { VoiceId = "voice-C" }];
        clock.Advance(TimeSpan.FromHours(2));
        Assert.Equal("voice-C", Assert.Single(await catalog.GetAsync(false, CancellationToken.None)).VoiceId);
        var newProcess = new EdgeVoiceCatalog(transport, clock);
        Assert.Empty(newProcess.Current);
        var saved = new SpeechProviderInstance(ProviderId.New(), "Microsoft Edge", 0,
            new EdgeSpeechProviderConfiguration(Voice), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        Assert.True(ProviderConfigurationValidator.Validate(saved).IsValid);
    }

    [Fact]
    public void Fingerprint_tracks_only_voice_identity_and_synthesis_contract()
    {
        var configuration = new EdgeSpeechProviderConfiguration(Voice);
        var baseline = ProviderSynthesisFingerprint.Create(configuration);
        Assert.Equal(baseline, ProviderSynthesisFingerprint.Create(configuration with
        {
            Voice = Voice with { FriendlyName = "Renamed", Locale = "en-US", Gender = "Male" }
        }));
        Assert.NotEqual(baseline, ProviderSynthesisFingerprint.Create(configuration with
        {
            Voice = Voice with { VoiceId = "voice-B" }
        }));
        Assert.NotEqual(baseline, ProviderSynthesisFingerprint.Create(configuration,
            ProviderSynthesisFingerprint.EdgeSynthesisContractVersion + 1));
    }

    private sealed class Transport : IEdgeSpeechTransport
    {
        public int Rate { get; private set; }
        public EdgeVoice? LastVoice { get; private set; }
        public IReadOnlyList<EdgeVoice> Voices { get; set; } = [];
        public bool ThrowOnCatalog { get; set; }
        public ProviderSynthesisFailure? Failure { get; set; }
        public Task<IReadOnlyList<EdgeVoice>> GetVoicesAsync(CancellationToken cancellationToken) =>
            ThrowOnCatalog ? throw new InvalidOperationException() : Task.FromResult(Voices);
        public Task<ProviderSynthesisResult> SynthesizeAsync(EdgeVoice voice, string text, int ratePercent,
            CancellationToken cancellationToken)
        {
            Rate = ratePercent;
            LastVoice = voice;
            return Task.FromResult(Failure is null ? new ProviderSynthesisResult(new MemoryStream([1]), "audio/mpeg", null, "mp3")
                : new ProviderSynthesisResult(null, null, Failure));
        }
    }

    private sealed class SettingsStore : IAppSettingsStore
    {
        public bool FailSave { get; set; }
        public CancellationTokenSource? CancelSave { get; set; }
        public AppSettings? Current { get; private set; }
        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(AppSettings.Default);
        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
        {
            if (FailSave) throw new IOException();
            CancelSave?.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            Current = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class ProviderStore : IProviderStore
    {
        public List<SpeechProviderInstance> Items { get; } = [];
        public Task<IReadOnlyList<SpeechProviderInstance>> GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SpeechProviderInstance>>(Items.ToArray());
        public Task<SpeechProviderInstance?> GetByIdAsync(ProviderId id, CancellationToken cancellationToken) =>
            Task.FromResult(Items.Find(item => item.Id == id));
        public Task SaveAsync(SpeechProviderInstance provider, CancellationToken cancellationToken)
        {
            Items.RemoveAll(item => item.Id == provider.Id);
            Items.Add(provider);
            return Task.CompletedTask;
        }
        public Task InsertAfterAsync(SpeechProviderInstance provider, ProviderId precedingId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task UpdateSortOrderAsync(IReadOnlyList<ProviderId> ids, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DeleteAsync(ProviderId id, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
