namespace NovelSpeaker.Application.Speech.Rules;

public sealed class TtsRuleChangedEventArgs(long ruleId) : EventArgs
{
    public long RuleId { get; } = ruleId;
}
