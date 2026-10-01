using System.Text.Json;

namespace NovelSpeaker.Application.Books.RuleEditing;

/// <summary>Validates rule document framing; each workspace validates its own item schema.</summary>
internal static class RuleExchangeDocument
{
    public static IReadOnlyList<JsonElement> Read(string json, string ruleType)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidOperationException("规则 JSON 不能为空。");
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            // Keep reading the published unversioned single-rule and array formats.
            if (root.ValueKind == JsonValueKind.Array)
            {
                return root.EnumerateArray().Select(item => item.Clone()).ToArray();
            }

            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException("规则 JSON 结构无效。");
            }

            if (!root.TryGetProperty("schemaVersion", out var version))
            {
                return [root.Clone()];
            }

            if (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var number) || number != 1 ||
                !root.TryGetProperty("ruleType", out var type) || type.ValueKind != JsonValueKind.String ||
                type.GetString() != ruleType ||
                !root.TryGetProperty("rules", out var rules) || rules.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException("规则文档版本或类型不受支持。");
            }

            return rules.EnumerateArray().Select(item => item.Clone()).ToArray();
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("规则 JSON 格式无效。", exception);
        }
    }
}
