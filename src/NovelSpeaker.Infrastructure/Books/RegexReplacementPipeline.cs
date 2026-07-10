using System.Text.RegularExpressions;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Domain.Books;

namespace NovelSpeaker.Infrastructure.Books;

/// <summary>Safely projects already segmented text into display and speech variants.</summary>
public sealed class RegexReplacementPipeline : IRegexReplacementPipeline
{
    private static readonly TimeSpan RuleTimeout = TimeSpan.FromMilliseconds(100);
    private readonly IRegexReplacementRuleRepository _repository;
    private readonly IRegexReplacementRuleErrorStore? _errorStore;

    public RegexReplacementPipeline(IRegexReplacementRuleRepository repository, IRegexReplacementRuleErrorStore? errorStore = null)
    {
        _repository = repository;
        _errorStore = errorStore;
    }

    public async Task<RegexReplacementPipelineResult> ApplyAsync(IReadOnlyList<SpeechSegment> sourceSegments, CancellationToken cancellationToken)
    {
        var rules = (await _repository.GetAllAsync(cancellationToken)).Where(rule => rule.IsEnabled).OrderBy(rule => rule.SortOrder).ThenBy(rule => rule.Id).ToArray();
        var errors = new Dictionary<Guid, string>();
        var output = new List<SpeechSegment>(sourceSegments.Count);
        foreach (var source in sourceSegments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var display = Apply(source.DisplayText, rules, RegexReplacementScope.Display, errors);
            var speech = Apply(source.SpeechText, rules, RegexReplacementScope.Speech, errors);
            if (string.IsNullOrEmpty(display) && string.IsNullOrEmpty(speech)) continue;
            output.Add(new SpeechSegment(output.Count, source.StartOffset, source.Length, display, speech));
        }
        _errorStore?.Replace(errors);
        return new RegexReplacementPipelineResult(output, errors);
    }

    private static string Apply(string value, IReadOnlyList<RegexReplacementRule> rules, RegexReplacementScope scope, IDictionary<Guid, string> errors)
    {
        foreach (var rule in rules)
        {
            if (rule.Scope is not RegexReplacementScope.Both && rule.Scope != scope) continue;
            try { value = new Regex(rule.Pattern, RegexOptions.CultureInvariant, RuleTimeout).Replace(value, rule.Replacement); }
            catch (RegexMatchTimeoutException) { errors.TryAdd(rule.Id, "执行超时，已跳过当前段。"); }
            catch (ArgumentException) { errors.TryAdd(rule.Id, "规则格式无效，已跳过。"); }
        }
        return value;
    }
}
