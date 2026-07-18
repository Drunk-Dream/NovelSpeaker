namespace NovelSpeaker.Application.Speech.Rules;

/// <summary>Converts an external JSON source into typed candidate rules without exposing JSON technology.</summary>
public interface ITtsRuleSourceAdapter
{
    TtsRuleSourceReadResult Read(string jsonText);
}

public sealed record TtsRuleSourceReadResult(IReadOnlyList<TtsRuleSourceItem> Items, string? ErrorMessage);

public sealed record TtsRuleSourceItem(int Index, TtsRuleConversionResult? Conversion, string? ErrorMessage);
