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
    public async Task Draft_preview_uses_unsaved_config_and_current_speed_without_changing_selection()
    {
        var current = ProviderId.New();
        var settings = new FakeSettings(AppSettings.Default with
        {
            CurrentProviderId = current,
            DefaultSpeakSpeed = 13
        });
        var runtime = new CapturingRuntime();
        var service = new HttpProviderDraftPreviewService([runtime], settings);
        var draft = CreateProvider("Unsaved") with
        {
            Configuration = new HttpSpeechProviderConfiguration(
                "https://example.com/unsaved", "GET", new Dictionary<string, string>(), null, null)
        };

        var result = await service.PreviewAsync(draft, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Same(draft, runtime.Provider);
        Assert.Equal(HttpProviderDraftPreviewService.PreviewText, runtime.Request!.Text);
        Assert.Equal(13, runtime.Request.SpeakSpeed);
        Assert.Equal(current, settings.Current.CurrentProviderId);
        Assert.Equal(0, settings.UpdateCount);
        await result.Audio!.DisposeAsync();
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
}
