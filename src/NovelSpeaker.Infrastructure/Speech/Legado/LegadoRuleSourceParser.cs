using System.Text.Json;

namespace NovelSpeaker.Infrastructure.Speech.Legado;

public sealed class LegadoRuleSourceParser
{
    private static readonly HashSet<string> SupportedFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "name",
        "url",
        "contentType",
        "concurrentRate",
        "header",
        "requestOptions",
        "isEnabled",
        "lastUpdateTime"
    };

    internal LegadoRuleSourceParseResult Parse(string jsonText)
    {
        if (string.IsNullOrWhiteSpace(jsonText))
        {
            return LegadoRuleSourceParseResult.Failure("没有可导入的 JSON 内容。");
        }

        try
        {
            using var document = JsonDocument.Parse(jsonText);
            var root = document.RootElement;
            if (root.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
            {
                return LegadoRuleSourceParseResult.Failure("规则 JSON 必须是对象或对象数组。");
            }

            if (root.ValueKind == JsonValueKind.Object)
            {
                return LegadoRuleSourceParseResult.Success([
                    LegadoRuleSourceItem.Valid(0, LegadoRuleSourceDto.FromJson(root, SupportedFields))
                ]);
            }

            var items = new List<LegadoRuleSourceItem>();
            var index = 0;
            foreach (var element in root.EnumerateArray())
            {
                items.Add(element.ValueKind == JsonValueKind.Object
                    ? LegadoRuleSourceItem.Valid(index, LegadoRuleSourceDto.FromJson(element, SupportedFields))
                    : LegadoRuleSourceItem.Invalid(index, "规则数组中的每一项都必须是对象。"));
                index++;
            }

            return LegadoRuleSourceParseResult.Success(items);
        }
        catch (JsonException)
        {
            return LegadoRuleSourceParseResult.Failure("JSON 解析失败，请检查规则内容。");
        }
    }
}

internal sealed record LegadoRuleSourceParseResult(
    IReadOnlyList<LegadoRuleSourceItem> Items,
    string? ErrorMessage)
{
    public static LegadoRuleSourceParseResult Success(IReadOnlyList<LegadoRuleSourceItem> items) => new(items, null);

    public static LegadoRuleSourceParseResult Failure(string errorMessage) => new([], errorMessage);
}

internal sealed record LegadoRuleSourceItem(
    int Index,
    LegadoRuleSourceDto? Source,
    string? ErrorMessage)
{
    public static LegadoRuleSourceItem Valid(int index, LegadoRuleSourceDto source) => new(index, source, null);

    public static LegadoRuleSourceItem Invalid(int index, string errorMessage) => new(index, null, errorMessage);
}
