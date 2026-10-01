using NovelSpeaker.Application.Settings;
using NovelSpeaker.Application.Speech.Providers;
using NovelSpeaker.Domain.Settings;
using NovelSpeaker.Domain.Speech.Providers;
using Xunit;

namespace NovelSpeaker.Application.UnitTests;

public sealed class HttpProviderWorkflowTests
{
    [Fact]
    public void Envelope_round_trip_keeps_complete_http_configuration_without_instance_metadata()
    {
        var provider = CreateProvider("HTTP Provider") with
        {
            Configuration = new HttpSpeechProviderConfiguration(
                "https://example.com/tts?key=secret", "POST",
                new Dictionary<string, string> { ["Cookie"] = "session=secret", ["Content-Type"] = "application/json" },
                """{"text":{{JSON.stringify(speakText)}}}""",
                new ProviderRequestRateLimit(2, 1000))
        };

        var json = ProviderEnvelopeCodec.Write(provider);
        var read = ProviderEnvelopeCodec.Read(json);

        Assert.True(read.IsValid);
        var item = Assert.Single(read.Items);
        Assert.True(item.IsValid);
        Assert.Equal(provider.Name, item.Name);
        Assert.Equal("session=secret", item.Configuration!.Headers["Cookie"]);
        Assert.Equal(new ProviderRequestRateLimit(2, 1000), item.Configuration.RateLimit);
        Assert.DoesNotContain(provider.Id.ToString(), json, StringComparison.Ordinal);
        Assert.DoesNotContain("sortOrder", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("createdAt", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("currentProvider", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Envelope_reads_each_provider_type_and_preserves_valid_items_after_invalid_ones()
    {
        var json = """
            {
              "schemaVersion": 1,
              "providers": [
                { "providerType": "edge", "name": "Unsupported", "configuration": {} },
                { "providerType": "http", "name": "Good", "configuration": {
                  "urlTemplate": "https://example.com/audio", "method": "GET", "headers": {}
                } },
                { "providerType": "http", "name": "Broken", "configuration": {
                  "urlTemplate": "https://example.com/audio", "method": "POST", "headers": {},
                  "bodyTemplate": 5
                } }
              ]
            }
            """;

        var read = ProviderEnvelopeCodec.Read(json);

        Assert.True(read.IsValid);
        Assert.Equal(3, read.Items.Count);
        Assert.False(read.Items[0].IsValid);
        Assert.True(read.Items[1].IsValid);
        Assert.False(read.Items[2].IsValid);
        Assert.False(ProviderEnvelopeCodec.Read("""{"name":"Legacy Rule"}""").IsValid);
    }

    [Fact]
    public void Portable_comparison_normalizes_name_method_and_header_case_and_order()
    {
        var left = new HttpSpeechProviderConfiguration("https://example.com/audio", "POST",
            new Dictionary<string, string>
            {
                ["Content-Type"] = "application/json",
                ["X-Key"] = "secret"
            }, "{}", new ProviderRequestRateLimit(2, 1000));
        var same = left with
        {
            Method = "post",
            Headers = new Dictionary<string, string>
            {
                ["x-key"] = "secret",
                ["content-type"] = "application/json"
            }
        };

        Assert.True(HttpProviderConfigurationComparer.IsSamePortableProvider(
            "Voice", left, "voice", same));
        Assert.False(HttpProviderConfigurationComparer.IsSamePortableProvider(
            "Voice", left, "Different", same));
        Assert.False(HttpProviderConfigurationComparer.IsSamePortableProvider(
            "Voice", left, "voice", same with { RateLimit = null }));
    }

    [Fact]
    public void Import_plan_skips_exact_duplicates_and_appends_valid_items_in_file_order()
    {
        var existing = CreateProvider("Voice") with
        {
            SortOrder = 7,
            Configuration = new HttpSpeechProviderConfiguration(
                "https://example.com/old", "GET",
                new Dictionary<string, string> { ["X-Key"] = "secret" }, null, null)
        };
        var json = """
            {
              "schemaVersion": 1,
              "providers": [
                { "providerType": "http", "name": "voice", "configuration": {
                  "urlTemplate": "https://example.com/old", "method": "get",
                  "headers": { "x-key": "secret" }
                } },
                { "providerType": "http", "name": "Voice", "configuration": {
                  "urlTemplate": "https://example.com/new", "method": "GET", "headers": {}
                } },
                { "providerType": "http", "name": "Another", "configuration": {
                  "urlTemplate": "https://example.com/new", "method": "GET", "headers": {}
                } },
                { "providerType": "http", "name": "Voice", "configuration": {
                  "urlTemplate": "https://example.com/new", "method": "GET", "headers": {}
                } },
                { "providerType": "http", "name": "Voice (2)", "configuration": {
                  "urlTemplate": "https://example.com/new", "method": "GET", "headers": {}
                } },
                { "providerType": "edge", "name": "Unsupported", "configuration": {} }
              ]
            }
            """;

        var plan = ProviderImportPlanner.Plan(json, [existing], DateTimeOffset.UnixEpoch);

        Assert.Null(plan.Error);
        Assert.Equal(2, plan.ReadyCount);
        Assert.Equal(3, plan.DuplicateCount);
        Assert.Equal(1, plan.InvalidCount);
        var firstAdded = Assert.IsType<SpeechProviderInstance>(plan.Items[1].Candidate);
        var secondAdded = Assert.IsType<SpeechProviderInstance>(plan.Items[2].Candidate);
        Assert.Equal("Voice (2)", firstAdded.Name);
        Assert.Equal(8, firstAdded.SortOrder);
        Assert.Equal("Another", secondAdded.Name);
        Assert.Equal(9, secondAdded.SortOrder);
        Assert.Equal(7, existing.SortOrder);
    }

    [Fact]
    public async Task Draft_preview_uses_unsaved_config_and_current_speed_without_changing_selection()
    {
        var current = ProviderId.New();
        var settings = new FakeSettings(AppSettings.Default with
        {
            CurrentProviderId = current,
            DefaultSpeakSpeed = 13
        });
        var runtime = new CapturingRuntime();
        var player = new CapturingPreviewPlayer();
        var service = new ProviderDraftPreviewService([runtime], settings, player);
        string? playbackFailure = null;
        service.PlaybackFailed += (_, args) => playbackFailure = args.Message;
        var draft = CreateProvider("Unsaved") with
        {
            Configuration = new HttpSpeechProviderConfiguration(
                "https://example.com/unsaved", "GET", new Dictionary<string, string>(), null, null)
        };

        var failure = await service.PlayAsync(draft, CancellationToken.None);

        Assert.Null(failure);
        Assert.Same(draft, runtime.Provider);
        Assert.Equal(ProviderDraftPreviewService.PreviewText, runtime.Request!.Text);
        Assert.Equal(13, runtime.Request.SpeakSpeed);
        Assert.Equal(current, settings.Current.CurrentProviderId);
        Assert.Equal(0, settings.UpdateCount);
        Assert.Equal(1, player.PlayCount);
        player.RaiseFailure();
        Assert.Equal("试听播放中断。", playbackFailure);
    }

    [Fact]
    public async Task Workspace_creates_an_unsaved_unique_draft_and_saves_new_and_existing_providers()
    {
        var existing = CreateProvider("HTTP Provider") with { SortOrder = 4 };
        var store = new RecordingProviderStore(existing);
        var workspace = new SpeechProviderWorkspace(store, TimeProvider.System, new FakeSettings(AppSettings.Default));

        var draft = await workspace.CreateDraftAsync(CancellationToken.None);
        Assert.Equal("HTTP Provider (2)", draft.Name);
        Assert.Single(store.Items);

        var configured = draft with { Configuration = existing.Configuration };
        var saved = await workspace.SaveAsync(configured, true, CancellationToken.None);
        Assert.Equal(5, saved.SortOrder);
        Assert.NotEqual(draft.Id, saved.Id);
        Assert.Equal(2, store.Items.Count);

        var edited = await workspace.SaveAsync(saved with { Name = "Renamed", SortOrder = 99 },
            false, CancellationToken.None);
        Assert.Equal(saved.Id, edited.Id);
        Assert.Equal(saved.CreatedAt, edited.CreatedAt);
        Assert.Equal(5, edited.SortOrder);
        Assert.Equal("Renamed", store.Items.Single(item => item.Id == saved.Id).Name);
        await Assert.ThrowsAsync<InvalidOperationException>(() => workspace.SaveAsync(
            edited with { Name = "http provider" }, false, CancellationToken.None));
    }

    [Fact]
    public async Task Workspace_import_continues_after_one_save_fails_and_retries_same_portable_item()
    {
        var store = new RecordingProviderStore(CreateProvider("Existing") with { SortOrder = 3 })
        {
            FailNextSave = true
        };
        var workspace = new SpeechProviderWorkspace(store, TimeProvider.System, new FakeSettings(AppSettings.Default));
        var json = """
            {
              "schemaVersion": 1,
              "providers": [
                { "providerType": "http", "name": "Voice", "configuration": {
                  "urlTemplate": "https://example.com/voice", "method": "GET", "headers": {}
                } },
                { "providerType": "edge", "name": "Unsupported", "configuration": {} },
                { "providerType": "http", "name": "Voice", "configuration": {
                  "urlTemplate": "https://example.com/voice", "method": "get", "headers": {}
                } },
                { "providerType": "http", "name": "Voice", "configuration": {
                  "urlTemplate": "https://example.com/voice", "method": "GET", "headers": {}
                } }
              ]
            }
            """;

        var result = await workspace.ImportAsync(json, CancellationToken.None);

        Assert.Null(result.Error);
        Assert.Equal(1, result.ImportedCount);
        Assert.Equal(1, result.DuplicateCount);
        Assert.Equal(2, result.FailedCount);
        Assert.Equal(ProviderImportExecutionStatus.Failed, result.Items[0].Status);
        Assert.Equal(ProviderImportExecutionStatus.Imported, result.Items[2].Status);
        Assert.Equal("Voice", store.Items.Single(item => item.Name == "Voice").Name);
        Assert.Equal(4, store.Items.Single(item => item.Name == "Voice").SortOrder);
    }

    [Fact]
    public async Task Workspace_requires_credential_warning_before_exporting_full_configuration()
    {
        var provider = CreateProvider("Secret") with
        {
            Configuration = new HttpSpeechProviderConfiguration(
                "https://example.com/audio", "GET",
                new Dictionary<string, string> { ["Cookie"] = "session=secret" }, null, null)
        };
        var workspace = new SpeechProviderWorkspace(new RecordingProviderStore(provider), TimeProvider.System, new FakeSettings(AppSettings.Default));

        var warning = await workspace.ExportAsync(provider.Id, false, CancellationToken.None);
        var exported = await workspace.ExportAsync(provider.Id, true, CancellationToken.None);

        Assert.Equal(ProviderExportStatus.ConfirmationRequired, warning.Status);
        Assert.Null(warning.Json);
        Assert.Contains("Cookie", warning.Message);
        Assert.Equal(ProviderExportStatus.Ready, exported.Status);
        Assert.Equal("session=secret", Assert.Single(ProviderEnvelopeCodec.Read(exported.Json!).Items)
            .Configuration!.Headers["Cookie"]);

        var importedStore = new RecordingProviderStore();
        var imported = await new SpeechProviderWorkspace(importedStore, TimeProvider.System, new FakeSettings(AppSettings.Default))
            .ImportAsync(exported.Json!, CancellationToken.None);
        Assert.Equal(1, imported.ImportedCount);
        var copy = Assert.Single(importedStore.Items);
        Assert.NotEqual(provider.Id, copy.Id);
        Assert.True(HttpProviderConfigurationComparer.IsSamePortableProvider(
            provider.Name, (HttpSpeechProviderConfiguration)provider.Configuration,
            copy.Name, (HttpSpeechProviderConfiguration)copy.Configuration));
    }

    [Fact]
    public async Task Bulk_export_keeps_credentials_and_stable_order_excludes_edge_and_requires_warning()
    {
        var first = CreateProvider("First") with
        {
            SortOrder = 10,
            Configuration = new HttpSpeechProviderConfiguration("https://example.com/audio", "GET",
                new Dictionary<string, string> { ["Authorization"] = "Bearer fixture-token" }, null, null)
        };
        var second = CreateProvider("Second") with { SortOrder = 30 };
        var edge = new SpeechProviderInstance(ProviderId.New(), "Microsoft Edge", 20,
            new EdgeSpeechProviderConfiguration(null), DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        var settings = new FakeSettings(AppSettings.Default with { CurrentProviderId = first.Id });
        var workspace = new SpeechProviderWorkspace(new RecordingProviderStore(first, edge, second), TimeProvider.System, settings);

        var warning = await workspace.ExportAsync([second.Id, edge.Id, first.Id], false, CancellationToken.None);
        Assert.Equal(ProviderExportStatus.ConfirmationRequired, warning.Status);
        Assert.Null(warning.Json);
        var exported = await workspace.ExportAsync([second.Id, edge.Id, first.Id], true, CancellationToken.None);
        var items = ProviderEnvelopeCodec.Read(exported.Json!).Items;
        Assert.Equal(["First", "Second"], items.Select(item => item.Name));
        Assert.Equal("Bearer fixture-token", items[0].Configuration!.Headers["Authorization"]);
        var imported = await workspace.ImportAsync(exported.Json!, CancellationToken.None);
        Assert.Equal(2, imported.DuplicateCount);
        Assert.Equal(first.Id, settings.Current.CurrentProviderId);
        Assert.Equal(ProviderExportStatus.ProviderUnavailable,
            (await workspace.ExportAsync([edge.Id], true, CancellationToken.None)).Status);
    }

    private static SpeechProviderInstance CreateProvider(string name) =>
        new(ProviderId.New(), name, 0,
            new HttpSpeechProviderConfiguration("https://example.com/audio", "GET",
                new Dictionary<string, string>(), null, null),
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private sealed class CapturingRuntime : IProviderRuntime
    {
        public SpeechProviderType Type => SpeechProviderType.Http;
        public SpeechProviderInstance? Provider { get; private set; }
        public ProviderSynthesisRequest? Request { get; private set; }

        public Task<ProviderSynthesisResult> SynthesizeAsync(
            SpeechProviderInstance provider,
            ProviderSynthesisRequest request,
            CancellationToken cancellationToken)
        {
            Provider = provider;
            Request = request;
            return Task.FromResult(new ProviderSynthesisResult(new MemoryStream([1]), "audio/wav", null));
        }
    }

    private sealed class FakeSettings(AppSettings initial) : IAppSettingsService
    {
        public AppSettings Current { get; private set; } = initial;
        public int UpdateCount { get; private set; }
        public event EventHandler<AppSettingsChangedEventArgs>? Changed
        {
            add { }
            remove { }
        }

        public Task<AppSettings> UpdateAsync(AppSettingsUpdate update, CancellationToken cancellationToken)
        {
            UpdateCount++;
            return Task.FromResult(Current);
        }
    }

    private sealed class CapturingPreviewPlayer : IProviderPreviewAudioPlayer
    {
        public int PlayCount { get; private set; }
        public event EventHandler<ProviderPreviewPlaybackFailedEventArgs>? PlaybackFailed;

        public Task<ProviderPreviewPlaybackResult> PlayAsync(
            Stream audio, string? audioFormat, CancellationToken cancellationToken)
        {
            PlayCount++;
            return Task.FromResult(new ProviderPreviewPlaybackResult(true, null));
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void RaiseFailure() =>
            PlaybackFailed?.Invoke(this, new ProviderPreviewPlaybackFailedEventArgs("试听播放中断。"));
    }

    private sealed class RecordingProviderStore(params SpeechProviderInstance[] initial) : IProviderStore
    {
        public List<SpeechProviderInstance> Items { get; } = [.. initial];
        public bool FailNextSave { get; set; }

        public Task<IReadOnlyList<SpeechProviderInstance>> GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SpeechProviderInstance>>(Items.ToList());

        public Task<SpeechProviderInstance?> GetByIdAsync(ProviderId providerId, CancellationToken cancellationToken) =>
            Task.FromResult(Items.FirstOrDefault(item => item.Id == providerId));

        public Task SaveAsync(SpeechProviderInstance provider, CancellationToken cancellationToken)
        {
            if (FailNextSave)
            {
                FailNextSave = false;
                throw new InvalidOperationException("Simulated store failure");
            }

            Items.RemoveAll(item => item.Id == provider.Id);
            Items.Add(provider);
            return Task.CompletedTask;
        }

        public Task InsertAfterAsync(SpeechProviderInstance provider, ProviderId precedingId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task UpdateSortOrderAsync(IReadOnlyList<ProviderId> orderedIds, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task DeleteAsync(ProviderId providerId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
