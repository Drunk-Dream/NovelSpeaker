using System.Text.Json;
using NovelSpeaker.Domain.Speech;

namespace NovelSpeaker.Application.Speech;

/// <summary>
/// Converts imported Legado-style rule payloads into the application's persisted rule format.
/// </summary>
public interface ITtsRuleConverter
{
    TtsRuleConversionResult Convert(JsonElement ruleElement);
}
