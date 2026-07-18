namespace NovelSpeaker.Application.Speech.Testing;

/// <summary>Reports rule-test orchestration failures through a safe technical diagnostics boundary.</summary>
public interface ITtsRuleTestFailureReporter
{
    void Report(string operation, Exception exception, TtsRuleDraftTestInput input);
}
