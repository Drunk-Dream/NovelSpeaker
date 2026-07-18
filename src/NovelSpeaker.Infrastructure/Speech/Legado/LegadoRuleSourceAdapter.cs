using NovelSpeaker.Application.Speech.Rules;

namespace NovelSpeaker.Infrastructure.Speech.Legado;

/// <summary>Owns JSON source parsing and Legado-to-business-rule conversion.</summary>
public sealed class LegadoRuleSourceAdapter(
    LegadoRuleSourceParser parser,
    LegadoRuleConverter converter) : ITtsRuleSourceAdapter
{
    public TtsRuleSourceReadResult Read(string jsonText)
    {
        var parsed = parser.Parse(jsonText);
        if (parsed.ErrorMessage is not null)
        {
            return new TtsRuleSourceReadResult([], parsed.ErrorMessage);
        }

        return new TtsRuleSourceReadResult(parsed.Items.Select(item => item.Source is null
            ? new TtsRuleSourceItem(item.Index, null, item.ErrorMessage)
            : new TtsRuleSourceItem(item.Index, converter.Convert(item.Source), null)).ToArray(), null);
    }
}
