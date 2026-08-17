using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Books.TextProcessing;
using NovelSpeaker.Application.Cache;
using NovelSpeaker.Application.Playback.Cache;
using NovelSpeaker.Domain.Books;
using Xunit;

namespace NovelSpeaker.Application.UnitTests;

public sealed class ChapterSpeechPlanServiceTests
{
    [Fact]
    public async Task BuildAsync_persists_body_identities_and_keeps_title_out_of_the_plan()
    {
        var pipeline = new FixedPipeline(
            [
                new SpeechSegment(9, 0, 3, "第一段", "第一段"),
                new SpeechSegment(10, 3, 3, "第二段", "第二段"),
                new SpeechSegment(11, 0, 0, "第一章", "第一章", IsChapterTitle: true)
            ]);
        var store = new RecordingStore();
        var service = CreateService(pipeline, store);

        var result = await service.BuildAsync(
            "chapter-1",
            "第一段\n第二段",
            TextSegmentationOptions.Default,
            CancellationToken.None);

        Assert.Equal(3, result.Segments.Count);
        Assert.Equal(2, result.Plan.BodySegmentCount);
        Assert.Equal([0, 1], result.Plan.Segments.Select(segment => segment.OrderIndex));
        Assert.Equal([0, 3], result.Plan.Segments.Select(segment => segment.SourceStartOffset));
        Assert.Same(result.Plan, Assert.Single(store.SavedPlans));
    }

    [Fact]
    public async Task BuildAsync_changes_only_the_affected_speech_hash_when_other_output_is_unchanged()
    {
        var firstSegments =
            new[]
            {
                new SpeechSegment(0, 0, 3, "第一段", "第一段"),
                new SpeechSegment(1, 3, 3, "第二段", "第二段")
            };
        var secondSegments =
            new[]
            {
                firstSegments[0],
                new SpeechSegment(0, 3, 3, "第二段", "第二段（已变更）")
            };
        var pipeline = new SequencePipeline(firstSegments, secondSegments);
        var store = new RecordingStore();
        var service = CreateService(pipeline, store);

        var first = await service.BuildAsync(
            "chapter-1",
            "第一段\n第二段",
            TextSegmentationOptions.Default,
            CancellationToken.None);
        var second = await service.BuildAsync(
            "chapter-1",
            "第一段\n第二段",
            TextSegmentationOptions.Default,
            CancellationToken.None);

        Assert.Equal(first.Plan.Segments[0].SpeechTextHash, second.Plan.Segments[0].SpeechTextHash);
        Assert.NotEqual(first.Plan.Segments[1].SpeechTextHash, second.Plan.Segments[1].SpeechTextHash);
        Assert.NotEqual(first.Plan.PlanOutputHash, second.Plan.PlanOutputHash);
    }

    [Fact]
    public async Task BuildAsync_excludes_body_segments_without_playable_speech_text_from_current_plan()
    {
        var pipeline = new FixedPipeline(
            [
                new SpeechSegment(0, 0, 1, "显示但被清空", "   "),
                new SpeechSegment(1, 1, 3, "章节分隔符", "………"),
                new SpeechSegment(2, 4, 1, "可播放", "可播放")
            ]);
        var store = new RecordingStore();
        var service = CreateService(pipeline, store);

        var result = await service.BuildAsync(
            "chapter-1",
            "原文",
            TextSegmentationOptions.Default,
            CancellationToken.None);

        Assert.Equal(1, result.Plan.BodySegmentCount);
        var segment = Assert.Single(result.Plan.Segments);
        Assert.Equal(4, segment.SourceStartOffset);
        Assert.Equal(Fingerprint.Sha256("可播放"), segment.SpeechTextHash);
    }

    [Fact]
    public async Task BuildAsync_preserves_cancellation_and_does_not_save_a_late_plan()
    {
        var pipeline = new BlockingPipeline();
        var store = new RecordingStore();
        var service = CreateService(pipeline, store);
        using var cancellation = new CancellationTokenSource();

        var build = service.BuildAsync(
            "chapter-1",
            "原文",
            TextSegmentationOptions.Default,
            cancellation.Token);
        await pipeline.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));

        cancellation.Cancel();
        pipeline.Release.TrySetResult(new RegexReplacementPipelineResult(
            [new SpeechSegment(0, 0, 2, "迟到结果", "迟到结果")],
            new Dictionary<Guid, string>(),
            []));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => build);
        Assert.Empty(store.SavedPlans);
    }

    private static ChapterSpeechPlanService CreateService(
        IRegexReplacementPipeline pipeline,
        IChapterSpeechPlanStore store) =>
        new(
            new TextSegmenter(),
            pipeline,
            new EmptyRuleRepository(),
            store,
            TimeProvider.System);

    private sealed class RecordingStore : IChapterSpeechPlanStore
    {
        public List<ChapterSpeechPlan> SavedPlans { get; } = [];

        public Task<ChapterSpeechPlan?> GetAsync(string chapterId, CancellationToken cancellationToken) =>
            Task.FromResult<ChapterSpeechPlan?>(null);

        public Task SaveAsync(ChapterSpeechPlan plan, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SavedPlans.Add(plan);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedPipeline(IReadOnlyList<SpeechSegment> segments) : IRegexReplacementPipeline
    {
        public Task<RegexReplacementPipelineResult> ApplyAsync(
            IReadOnlyList<SpeechSegment> sourceSegments,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new RegexReplacementPipelineResult(
                segments,
                new Dictionary<Guid, string>(),
                []));
        }
    }

    private sealed class SequencePipeline(
        IReadOnlyList<SpeechSegment> first,
        IReadOnlyList<SpeechSegment> second) : IRegexReplacementPipeline
    {
        private int _calls;

        public Task<RegexReplacementPipelineResult> ApplyAsync(
            IReadOnlyList<SpeechSegment> sourceSegments,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var segments = Interlocked.Increment(ref _calls) == 1 ? first : second;
            return Task.FromResult(new RegexReplacementPipelineResult(
                segments,
                new Dictionary<Guid, string>(),
                []));
        }
    }

    private sealed class BlockingPipeline : IRegexReplacementPipeline
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<RegexReplacementPipelineResult> Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<RegexReplacementPipelineResult> ApplyAsync(
            IReadOnlyList<SpeechSegment> sourceSegments,
            CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            return Release.Task;
        }
    }

    private sealed class EmptyRuleRepository : IRegexReplacementRuleRepository
    {
        public Task<IReadOnlyList<RegexReplacementRule>> GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RegexReplacementRule>>([]);

        public Task SaveAsync(RegexReplacementRule rule, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task UpdateEnabledAsync(Guid ruleId, bool isEnabled, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SaveOrderAsync(IReadOnlyList<(Guid RuleId, int SortOrder)> order, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task DeleteAsync(Guid ruleId, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
