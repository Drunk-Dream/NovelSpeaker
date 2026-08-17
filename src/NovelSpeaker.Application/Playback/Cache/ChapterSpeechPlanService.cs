using System.Globalization;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Books.TextProcessing;
using NovelSpeaker.Application.Cache;
using NovelSpeaker.Domain.Books;

namespace NovelSpeaker.Application.Playback.Cache;

/// <summary>
/// Creates one deterministic current plan and atomically hands it to the persistence port.
/// </summary>
public sealed class ChapterSpeechPlanService : IChapterSpeechPlanService
{
    private readonly ITextSegmenter _textSegmenter;
    private readonly IRegexReplacementPipeline _regexReplacementPipeline;
    private readonly IRegexReplacementRuleRepository _ruleRepository;
    private readonly IChapterSpeechPlanStore _store;
    private readonly TimeProvider _timeProvider;

    public ChapterSpeechPlanService(
        ITextSegmenter textSegmenter,
        IRegexReplacementPipeline regexReplacementPipeline,
        IRegexReplacementRuleRepository ruleRepository,
        IChapterSpeechPlanStore store,
        TimeProvider timeProvider)
    {
        _textSegmenter = textSegmenter;
        _regexReplacementPipeline = regexReplacementPipeline;
        _ruleRepository = ruleRepository;
        _store = store;
        _timeProvider = timeProvider;
    }

    public async Task<ChapterSpeechPlanBuildResult> BuildAsync(
        string chapterId,
        string chapterText,
        TextSegmentationOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chapterId);
        ArgumentNullException.ThrowIfNull(chapterText);

        var normalizedOptions = options.Normalize();
        var sourceSegments = await Task.Run(
            () => _textSegmenter.Segment(chapterText, normalizedOptions),
            cancellationToken).ConfigureAwait(false);
        var replaced = await _regexReplacementPipeline
            .ApplyAsync(sourceSegments, cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        var rules = replaced.AppliedRules ??
            await _ruleRepository.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var textProfile = TextProfileFingerprint.Create(normalizedOptions, rules);
        var bodySegments = replaced.Segments
            .Where(segment =>
                !segment.IsChapterTitle &&
                NarratableText.HasContent(segment.SpeechText))
            .ToArray();
        var planSegments = bodySegments
            .Select((segment, orderIndex) => new ChapterSpeechPlanSegment(
                orderIndex,
                segment.SegmentKind,
                segment.StartOffset,
                segment.Length,
                Fingerprint.Sha256(segment.SpeechText)))
            .ToArray();
        var plan = new ChapterSpeechPlan(
            chapterId,
            Fingerprint.Sha256(chapterText),
            textProfile,
            BuildPlanOutputHash(planSegments),
            ChapterSpeechPlanState.Ready,
            planSegments.Length,
            _timeProvider.GetUtcNow(),
            planSegments);

        await _store.SaveAsync(plan, cancellationToken).ConfigureAwait(false);
        return new ChapterSpeechPlanBuildResult(replaced.Segments, plan);
    }

    private static Fingerprint BuildPlanOutputHash(
        IReadOnlyList<ChapterSpeechPlanSegment> segments)
    {
        var writer = new CanonicalIdentityWriter();
        writer.Add("schema", "chapter-speech-plan-v1");
        writer.Add("count", segments.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var segment in segments)
        {
            writer.Add("order", segment.OrderIndex.ToString(CultureInfo.InvariantCulture));
            writer.Add("kind", ((int)segment.SegmentKind).ToString(CultureInfo.InvariantCulture));
            writer.Add("start", segment.SourceStartOffset.ToString(CultureInfo.InvariantCulture));
            writer.Add("length", segment.SourceLength.ToString(CultureInfo.InvariantCulture));
            writer.Add("speech", segment.SpeechTextHash.Hex);
        }

        return writer.Build();
    }
}
