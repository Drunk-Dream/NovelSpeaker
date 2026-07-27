using NovelSpeaker.Domain.Books;

namespace NovelSpeaker.Application.Books.TextProcessing;

/// <summary>Safely projects segmented text into independent display and speech variants.</summary>
public sealed class RegexReplacementPipeline : IRegexReplacementPipeline
{
    private readonly IRegexReplacementRuleRepository _repository;
    private readonly IRegexReplacementRuleErrorStore _errorStore;

    public RegexReplacementPipeline(
        IRegexReplacementRuleRepository repository,
        IRegexReplacementRuleErrorStore errorStore)
    {
        _repository = repository;
        _errorStore = errorStore;
    }

    public async Task<RegexReplacementPipelineResult> ApplyAsync(
        IReadOnlyList<SpeechSegment> sourceSegments,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sourceSegments);
        var rules = (await _repository.GetAllAsync(cancellationToken))
            .Where(rule => rule.IsEnabled)
            .OrderBy(rule => rule.SortOrder)
            .ThenBy(rule => rule.Id)
            .ToArray();
        var result = RegexReplacementProcessor.Apply(sourceSegments, rules, cancellationToken);
        _errorStore.Replace(result.RuleErrors);
        return result;
    }
}
