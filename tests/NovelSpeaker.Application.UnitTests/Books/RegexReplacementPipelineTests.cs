using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Books.TextProcessing;
using NovelSpeaker.Application.Playback.Cache;
using NovelSpeaker.Domain.Books;
using Xunit;

namespace NovelSpeaker.Application.UnitTests.Books;

public sealed class RegexReplacementPipelineTests
{
    [Fact]
    public async Task ApplyAsync_applies_display_and_speech_chains_independently_in_stable_order()
    {
        var displayId = Guid.NewGuid();
        var bothId = Guid.NewGuid();
        var pipeline = CreatePipeline(new FakeRepository(
        [
            Rule(displayId, 10, "a", "b", RegexReplacementScope.Display),
            Rule(bothId, 20, "b", "c", RegexReplacementScope.Both),
            Rule(Guid.NewGuid(), 30, "a", "x", RegexReplacementScope.Speech)
        ]));

        var result = await pipeline.ApplyAsync([new SpeechSegment(0, 7, 1, "a", "a")], CancellationToken.None);

        var segment = Assert.Single(result.Segments);
        Assert.Equal("c", segment.DisplayText);
        Assert.Equal("x", segment.SpeechText);
        Assert.Equal(7, segment.StartOffset);
        Assert.Empty(result.RuleErrors);
    }

    [Fact]
    public async Task ApplyAsync_orders_by_sort_order_and_id_and_ignores_disabled_rules()
    {
        var firstId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var secondId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var disabled = Rule(Guid.NewGuid(), 1, "a", "disabled", RegexReplacementScope.Both) with
        {
            IsEnabled = false
        };
        var pipeline = CreatePipeline(new FakeRepository(
        [
            Rule(secondId, 10, "b", "c", RegexReplacementScope.Both),
            disabled,
            Rule(firstId, 10, "a", "b", RegexReplacementScope.Both)
        ]));

        var result = await pipeline.ApplyAsync(
            [new SpeechSegment(0, 0, 1, "a", "a")],
            CancellationToken.None);

        var segment = Assert.Single(result.Segments);
        Assert.Equal("c", segment.DisplayText);
        Assert.Equal("c", segment.SpeechText);
    }

    [Fact]
    public async Task ApplyAsync_filters_only_segments_where_both_projections_are_empty_and_reindexes_runtime_list()
    {
        var pipeline = CreatePipeline(new FakeRepository(
        [Rule(Guid.NewGuid(), 10, "skip", "", RegexReplacementScope.Both)]));

        var result = await pipeline.ApplyAsync(
        [
            new SpeechSegment(0, 0, 4, "skip", "skip"),
            new SpeechSegment(1, 5, 4, "keep", "keep")
        ], CancellationToken.None);

        var segment = Assert.Single(result.Segments);
        Assert.Equal(0, segment.SegmentIndex);
        Assert.Equal(5, segment.StartOffset);
        Assert.Equal("keep", segment.DisplayText);
    }

    [Fact]
    public async Task ApplyAsync_skips_malformed_historical_rule_without_exposing_source_text()
    {
        var id = Guid.NewGuid();
        var pipeline = CreatePipeline(new FakeRepository([Rule(id, 10, "[", "", RegexReplacementScope.Both)]));

        var result = await pipeline.ApplyAsync([new SpeechSegment(0, 0, 6, "秘密正文", "秘密正文")], CancellationToken.None);

        Assert.Equal("秘密正文", Assert.Single(result.Segments).SpeechText);
        Assert.Contains(id, result.RuleErrors.Keys);
        Assert.DoesNotContain("秘密正文", result.RuleErrors[id]);
    }

    [Fact]
    public async Task ApplyAsync_keeps_single_projection_empty_so_the_consumer_can_skip_or_hide_it()
    {
        var pipeline = CreatePipeline(new FakeRepository(
        [
            Rule(Guid.NewGuid(), 10, "隐藏", string.Empty, RegexReplacementScope.Display),
            Rule(Guid.NewGuid(), 20, "静音", string.Empty, RegexReplacementScope.Speech)
        ]));

        var result = await pipeline.ApplyAsync(
        [
            new SpeechSegment(0, 0, 2, "隐藏", "隐藏"),
            new SpeechSegment(1, 3, 2, "静音", "静音")
        ],
        CancellationToken.None);

        Assert.Equal(2, result.Segments.Count);
        Assert.Equal(string.Empty, result.Segments[0].DisplayText);
        Assert.Equal("隐藏", result.Segments[0].SpeechText);
        Assert.Equal("静音", result.Segments[1].DisplayText);
        Assert.Equal(string.Empty, result.Segments[1].SpeechText);
    }

    [Fact]
    public async Task Final_speech_projection_drives_cache_identity_independently_from_display_projection()
    {
        var source = new[] { new SpeechSegment(0, 0, 1, "a", "a") };
        var displayResult = await CreatePipeline(new FakeRepository(
            [Rule(Guid.NewGuid(), 10, "a", "display-only", RegexReplacementScope.Display)]))
            .ApplyAsync(source, CancellationToken.None);
        var speechResult = await CreatePipeline(new FakeRepository(
            [Rule(Guid.NewGuid(), 10, "a", "speech-only", RegexReplacementScope.Speech)]))
            .ApplyAsync(source, CancellationToken.None);

        var displaySegment = Assert.Single(displayResult.Segments);
        var speechSegment = Assert.Single(speechResult.Segments);
        var baselineKey = TestAudioCacheKey.Create("book", 0, 0, 1, 10, "a");
        var displayKey = TestAudioCacheKey.Create("book", 0, 0, 1, 10, displaySegment.SpeechText);
        var speechKey = TestAudioCacheKey.Create("book", 0, 0, 1, 10, speechSegment.SpeechText);

        Assert.Equal("display-only", displaySegment.DisplayText);
        Assert.Equal(baselineKey, displayKey);
        Assert.NotEqual(baselineKey, speechKey);
    }

    [Fact]
    public async Task ApplyAsync_reports_safe_runtime_errors_to_the_workspace_error_store()
    {
        var id = Guid.NewGuid();
        var errors = new RegexReplacementRuleErrorStore();
        var pipeline = new RegexReplacementPipeline(
            new FakeRepository([Rule(id, 10, "[", string.Empty, RegexReplacementScope.Both)]),
            errors);

        await pipeline.ApplyAsync([new SpeechSegment(0, 0, 4, "正文", "正文")], CancellationToken.None);

        Assert.True(errors.Current.TryGetValue(id, out var message));
        Assert.DoesNotContain("正文", message);
    }

    [Fact]
    public async Task ApplyAsync_propagates_cancellation_without_replacing_current_errors()
    {
        var errorStore = new RegexReplacementRuleErrorStore();
        var existingError = new Dictionary<Guid, string> { [Guid.NewGuid()] = "existing" };
        errorStore.Replace(existingError);
        var pipeline = new RegexReplacementPipeline(new FakeRepository([]), errorStore);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pipeline.ApplyAsync(
            [new SpeechSegment(0, 0, 1, "a", "a")],
            cancellation.Token));

        Assert.Equal(existingError, errorStore.Current);
    }

    private static RegexReplacementRule Rule(Guid id, int order, string pattern, string replacement, RegexReplacementScope scope) =>
        new(id, id.ToString(), true, order, pattern, replacement, scope, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private static RegexReplacementPipeline CreatePipeline(IRegexReplacementRuleRepository repository)
    {
        return new RegexReplacementPipeline(repository, new RegexReplacementRuleErrorStore());
    }

    private sealed class FakeRepository : IRegexReplacementRuleRepository
    {
        private readonly IReadOnlyList<RegexReplacementRule> _rules;
        public FakeRepository(IReadOnlyList<RegexReplacementRule> rules) => _rules = rules;
        public Task<IReadOnlyList<RegexReplacementRule>> GetAllAsync(CancellationToken cancellationToken) => Task.FromResult(_rules);
        public Task SaveAsync(RegexReplacementRule rule, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task UpdateEnabledAsync(Guid ruleId, bool isEnabled, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SaveOrderAsync(IReadOnlyList<(Guid RuleId, int SortOrder)> order, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DeleteAsync(Guid ruleId, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
