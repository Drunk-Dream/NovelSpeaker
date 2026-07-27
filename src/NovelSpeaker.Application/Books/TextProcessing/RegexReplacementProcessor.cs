using System.Text.RegularExpressions;
using NovelSpeaker.Domain.Books;

namespace NovelSpeaker.Application.Books.TextProcessing;

/// <summary>
/// Applies one frozen ordered rule set to a segment collection.
/// </summary>
internal static class RegexReplacementProcessor
{
    private static readonly TimeSpan RuleTimeout = TimeSpan.FromMilliseconds(100);

    public static RegexReplacementPipelineResult Apply(
        IReadOnlyList<SpeechSegment> sourceSegments,
        IReadOnlyList<RegexReplacementRule> rules,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sourceSegments);
        ArgumentNullException.ThrowIfNull(rules);

        var errors = new Dictionary<Guid, string>();
        var output = new List<SpeechSegment>(sourceSegments.Count);
        foreach (var source in sourceSegments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var display = ApplyValue(
                source.DisplayText,
                rules,
                RegexReplacementScope.Display,
                errors);
            var speech = ApplyValue(
                source.SpeechText,
                rules,
                RegexReplacementScope.Speech,
                errors);
            if (string.IsNullOrEmpty(display) && string.IsNullOrEmpty(speech))
            {
                continue;
            }

            output.Add(new SpeechSegment(output.Count, source.StartOffset, source.Length, display, speech));
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new RegexReplacementPipelineResult(output, errors);
    }

    private static string ApplyValue(
        string value,
        IReadOnlyList<RegexReplacementRule> rules,
        RegexReplacementScope scope,
        IDictionary<Guid, string> errors)
    {
        foreach (var rule in rules)
        {
            if (rule.Scope is not RegexReplacementScope.Both && rule.Scope != scope)
            {
                continue;
            }

            try
            {
                value = new Regex(rule.Pattern, RegexOptions.CultureInvariant, RuleTimeout)
                    .Replace(value, rule.Replacement);
            }
            catch (RegexMatchTimeoutException)
            {
                errors.TryAdd(rule.Id, "执行超时，已跳过当前段。");
            }
            catch (ArgumentException)
            {
                errors.TryAdd(rule.Id, "规则格式无效，已跳过。");
            }
        }

        return value;
    }
}
