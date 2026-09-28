using NovelSpeaker.Application.Speech.Providers;
using NovelSpeaker.Domain.Speech.Providers;
using Xunit;

namespace NovelSpeaker.Application.UnitTests;

public sealed class ProviderFoundationTests
{
    [Fact]
    public void Legacy_rule_provider_id_is_stable_and_distinct_by_rule_id()
    {
        var first = ProviderId.FromLegacyHttpTtsRuleId(17);

        Assert.Equal(first, ProviderId.FromLegacyHttpTtsRuleId(17));
        Assert.NotEqual(first, ProviderId.FromLegacyHttpTtsRuleId(18));
    }

    [Fact]
    public void Provider_name_is_trimmed_and_Microsoft_Edge_is_reserved_case_insensitively()
    {
        Assert.True(SpeechProviderNameRules.TryNormalize("  Voice A  ", out var normalized));
        Assert.Equal("Voice A", normalized);
        Assert.False(SpeechProviderNameRules.TryNormalize("  ", out _));
        Assert.True(SpeechProviderNameRules.IsReserved("microsoft edge"));
    }

    [Fact]
    public void Synthesis_fingerprint_ignores_instance_identity_order_and_rate_limit_but_tracks_request_configuration()
    {
        var baseline = CreateProvider(
            ProviderId.New(),
            "Provider A",
            0,
            rateLimit: new ProviderRequestRateLimit(1, 500));
        var renamedAndReordered = baseline with
        {
            Id = ProviderId.New(),
            Name = "Provider B",
            SortOrder = 50,
            Configuration = ((HttpSpeechProviderConfiguration)baseline.Configuration) with
            {
                RateLimit = new ProviderRequestRateLimit(4, 1000)
            }
        };

        Assert.Equal(
            ProviderSynthesisFingerprint.Create(baseline),
            ProviderSynthesisFingerprint.Create(renamedAndReordered));

        var changedConfig = baseline with
        {
            Configuration = ((HttpSpeechProviderConfiguration)baseline.Configuration) with
            {
                BodyTemplate = "{\"text\":{{JSON.stringify(speakText)}}}"
            }
        };

        Assert.NotEqual(
            ProviderSynthesisFingerprint.Create(baseline),
            ProviderSynthesisFingerprint.Create(changedConfig));
    }

    [Fact]
    public async Task Runtime_resolver_returns_HTTP_runtime_and_safe_unavailable_states()
    {
        var id = ProviderId.New();
        var provider = CreateProvider(id, "Voice", 0);
        var runtime = new FakeRuntime();
        var resolver = new ProviderRuntimeResolver(new FakeProviderStore(provider), runtime);

        var resolved = await resolver.ResolveAsync(id, CancellationToken.None);
        var none = await resolver.ResolveAsync(null, CancellationToken.None);
        var missing = await resolver.ResolveAsync(ProviderId.New(), CancellationToken.None);

        Assert.True(resolved.IsAvailable);
        Assert.Same(runtime, resolved.Runtime);
        Assert.Equal(provider, resolved.Provider);
        Assert.Equal(ProviderRuntimeUnavailableReason.NoCurrentProvider, none.UnavailableReason);
        Assert.Equal(ProviderRuntimeUnavailableReason.ProviderNotFound, missing.UnavailableReason);
    }

    [Fact]
    public async Task Runtime_resolver_reports_missing_runtime_as_unavailable()
    {
        var provider = CreateProvider(ProviderId.New(), "Voice", 0);
        var resolver = new ProviderRuntimeResolver(new FakeProviderStore(provider));

        var result = await resolver.ResolveAsync(provider.Id, CancellationToken.None);

        Assert.False(result.IsAvailable);
        Assert.Equal(provider, result.Provider);
        Assert.Equal(ProviderRuntimeUnavailableReason.RuntimeUnavailable, result.UnavailableReason);
    }

    [Fact]
    public async Task Runtime_resolver_marks_invalid_provider_configuration_unavailable()
    {
        var provider = CreateProvider(ProviderId.New(), "Voice", 0) with
        {
            Configuration = new HttpSpeechProviderConfiguration(
                "https://example.invalid/tts",
                "GET",
                new Dictionary<string, string>(),
                "body",
                null)
        };
        var resolver = new ProviderRuntimeResolver(new FakeProviderStore(provider), new FakeRuntime());

        var result = await resolver.ResolveAsync(provider.Id, CancellationToken.None);

        Assert.False(result.IsAvailable);
        Assert.Equal(ProviderRuntimeUnavailableReason.ProviderNotConfigured, result.UnavailableReason);
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("file:///tmp/voice.wav")]
    [InlineData("file://{{speakText}}")]
    [InlineData("/api/{{speakText}}")]
    [InlineData("https://?token={{speakText}}")]
    public async Task Runtime_resolver_rejects_invalid_HTTP_URLs(string url)
    {
        var provider = CreateProvider(ProviderId.New(), "Voice", 0) with
        {
            Configuration = new HttpSpeechProviderConfiguration(
                url,
                "GET",
                new Dictionary<string, string>(),
                null,
                null)
        };
        var resolver = new ProviderRuntimeResolver(new FakeProviderStore(provider), new FakeRuntime());

        var result = await resolver.ResolveAsync(provider.Id, CancellationToken.None);

        Assert.False(result.IsAvailable);
        Assert.Equal(ProviderRuntimeUnavailableReason.ProviderNotConfigured, result.UnavailableReason);
    }

    [Fact]
    public async Task Runtime_resolver_allows_URL_templates_that_supply_the_absolute_address()
    {
        var provider = CreateProvider(ProviderId.New(), "Voice", 0) with
        {
            Configuration = new HttpSpeechProviderConfiguration(
                "{{'https://example.invalid/tts'}}",
                "GET",
                new Dictionary<string, string>(),
                null,
                null)
        };
        var resolver = new ProviderRuntimeResolver(new FakeProviderStore(provider), new FakeRuntime());

        var result = await resolver.ResolveAsync(provider.Id, CancellationToken.None);

        Assert.True(result.IsAvailable);
    }

    [Fact]
    public async Task Runtime_resolver_reports_malformed_persisted_configuration_as_unavailable()
    {
        var provider = CreateProvider(ProviderId.New(), "Voice", 0) with
        {
            Configuration = null!
        };
        var resolver = new ProviderRuntimeResolver(new FakeProviderStore(provider), new FakeRuntime());

        var result = await resolver.ResolveAsync(provider.Id, CancellationToken.None);

        Assert.False(result.IsAvailable);
        Assert.Equal(ProviderRuntimeUnavailableReason.ProviderNotConfigured, result.UnavailableReason);
    }

    [Fact]
    public async Task Runtime_resolver_reports_missing_HTTP_headers_as_unavailable()
    {
        var provider = CreateProvider(ProviderId.New(), "Voice", 0) with
        {
            Configuration = new HttpSpeechProviderConfiguration(
                "https://example.invalid/tts",
                "GET",
                null!,
                null,
                null)
        };
        var resolver = new ProviderRuntimeResolver(new FakeProviderStore(provider), new FakeRuntime());

        var result = await resolver.ResolveAsync(provider.Id, CancellationToken.None);

        Assert.False(result.IsAvailable);
        Assert.Equal(ProviderRuntimeUnavailableReason.ProviderNotConfigured, result.UnavailableReason);
    }

    [Fact]
    public async Task Runtime_resolver_reports_null_HTTP_header_values_as_unavailable()
    {
        var provider = CreateProvider(ProviderId.New(), "Voice", 0) with
        {
            Configuration = new HttpSpeechProviderConfiguration(
                "https://example.invalid/tts",
                "GET",
                new Dictionary<string, string> { ["X-Voice"] = null! },
                null,
                null)
        };
        var resolver = new ProviderRuntimeResolver(new FakeProviderStore(provider), new FakeRuntime());

        var result = await resolver.ResolveAsync(provider.Id, CancellationToken.None);

        Assert.False(result.IsAvailable);
        Assert.Equal(ProviderRuntimeUnavailableReason.ProviderNotConfigured, result.UnavailableReason);
    }

    [Fact]
    public void Provider_configuration_allows_legacy_words_as_payload_keys_and_string_values()
    {
        var provider = CreateProvider(ProviderId.New(), "Voice", 0) with
        {
            Configuration = new HttpSpeechProviderConfiguration(
                "https://example.invalid/tts",
                "POST",
                new Dictionary<string, string>(),
                "{{JSON.stringify({ source /* payload key */ : speakText, java: 'source' })}}",
                null)
        };

        Assert.True(ProviderConfigurationValidator.Validate(provider).IsValid);
    }

    [Theory]
    [InlineData("X Voice", "value")]
    [InlineData("X-Voice", "value\r\ninjected: true")]
    [InlineData("X-Voice", "value\u007f")]
    public void Provider_configuration_rejects_invalid_HTTP_headers(string headerName, string headerValue)
    {
        var provider = CreateProvider(ProviderId.New(), "Voice", 0) with
        {
            Configuration = new HttpSpeechProviderConfiguration(
                "https://example.invalid/tts",
                "GET",
                new Dictionary<string, string> { [headerName] = headerValue },
                null,
                null)
        };

        Assert.False(ProviderConfigurationValidator.Validate(provider).IsValid);
    }

    private static SpeechProviderInstance CreateProvider(
        ProviderId id,
        string name,
        int sortOrder,
        ProviderRequestRateLimit? rateLimit = null) =>
        new(
            id,
            name,
            sortOrder,
            new HttpSpeechProviderConfiguration(
                "https://example.invalid/tts?text={{encodeURIComponent(speakText)}}",
                "GET",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["X-Voice"] = "{{speakSpeed}}"
                },
                null,
                rateLimit),
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch);

    private sealed class FakeProviderStore(params SpeechProviderInstance[] providers) : IProviderStore
    {
        public Task<IReadOnlyList<SpeechProviderInstance>> GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SpeechProviderInstance>>(providers);

        public Task<SpeechProviderInstance?> GetByIdAsync(ProviderId providerId, CancellationToken cancellationToken) =>
            Task.FromResult(providers.FirstOrDefault(provider => provider.Id == providerId));

        public Task SaveAsync(SpeechProviderInstance provider, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task InsertAfterAsync(SpeechProviderInstance provider, ProviderId precedingId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task UpdateSortOrderAsync(IReadOnlyList<ProviderId> orderedIds, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task DeleteAsync(ProviderId providerId, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeRuntime : IProviderRuntime
    {
        public SpeechProviderType Type => SpeechProviderType.Http;

        public Task<ProviderSynthesisResult> SynthesizeAsync(
            SpeechProviderInstance provider,
            ProviderSynthesisRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
