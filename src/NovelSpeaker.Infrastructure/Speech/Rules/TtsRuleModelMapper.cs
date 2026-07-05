using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using NovelSpeaker.Application.Speech;
using NovelSpeaker.Domain.Speech;

namespace NovelSpeaker.Infrastructure.Speech.Rules;

internal static partial class TtsRuleModelMapper
{
    private static readonly JsonSerializerOptions SerializerOptions = new();

    public static TtsRuleEditorModel ToEditor(HttpTtsRule rule)
    {
        return new TtsRuleEditorModel(
            rule.Id,
            rule.Name,
            rule.IsEnabled,
            rule.Url,
            rule.ContentType,
            rule.ConcurrentRate,
            rule.LastUpdateTime,
            ParseKeyValueJson(rule.Header),
            ParseRequestOptions(rule.RequestOptionsJson));
    }

    public static TtsRuleSummary ToSummary(HttpTtsRule rule, long? selectedRuleId)
    {
        return new TtsRuleSummary(
            rule.Id,
            rule.Name,
            rule.IsEnabled,
            selectedRuleId == rule.Id && rule.IsEnabled,
            rule.LastUsedAt);
    }

    public static TtsRuleEditorModel NormalizeEditor(TtsRuleEditorModel editor)
    {
        return editor with
        {
            Name = editor.Name.Trim(),
            Url = editor.Url.Trim(),
            ContentType = NormalizeOptionalText(editor.ContentType),
            ConcurrentRate = NormalizeOptionalText(editor.ConcurrentRate),
            Headers = editor.Headers
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Key))
                .Select(entry => new TtsRuleEditorKeyValue(entry.Key.Trim(), entry.Value))
                .ToArray(),
            RequestOptions = editor.RequestOptions with
            {
                Method = NormalizeOptionalText(editor.RequestOptions.Method)?.ToUpperInvariant(),
                Body = NormalizeOptionalText(editor.RequestOptions.Body)
            }
        };
    }

    public static IReadOnlyList<string> Validate(TtsRuleEditorModel editor)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(editor.Name))
        {
            errors.Add("规则名称不能为空。");
        }

        if (string.IsNullOrWhiteSpace(editor.Url))
        {
            errors.Add("规则 URL 不能为空。");
        }
        else
        {
            TryValidateTemplate(editor.Url, "URL", errors);
        }

        if (!string.IsNullOrWhiteSpace(editor.ConcurrentRate) &&
            !ConcurrentRatePattern().IsMatch(editor.ConcurrentRate))
        {
            errors.Add("并发限制必须使用类似 2/1000 的格式。");
        }

        ValidateHeaders(editor.Headers, "Header", errors);
        ValidateRequestOptions(editor.RequestOptions, errors);
        return errors;
    }

    public static HttpTtsRule BuildRuleFromEditor(TtsRuleEditorModel editor, HttpTtsRule? existingRule)
    {
        var utcNow = DateTime.UtcNow.ToString("O");

        return new HttpTtsRule(
            editor.Id ?? 0,
            editor.Name,
            editor.Url,
            editor.ContentType,
            editor.ConcurrentRate,
            SerializeKeyValueJson(editor.Headers),
            SerializeRequestOptions(editor.RequestOptions),
            editor.LastUpdateTime,
            editor.IsEnabled,
            existingRule?.LastUsedAt,
            existingRule?.CreatedAt ?? utcNow,
            utcNow);
    }

    public static string ExportRuleJson(HttpTtsRule rule)
    {
        return NovelSpeakerRuleJsonSerializer.Serialize(rule);
    }

    private static string? NormalizeOptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static void ValidateHeaders(
        IReadOnlyList<TtsRuleEditorKeyValue> headers,
        string fieldName,
        List<string> errors)
    {
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in headers)
        {
            if (string.IsNullOrWhiteSpace(header.Key))
            {
                errors.Add($"{fieldName} 中存在空键名。");
                continue;
            }

            if (!seenKeys.Add(header.Key))
            {
                errors.Add($"{fieldName} 中存在重复键：{header.Key}");
            }

            TryValidateTemplate(header.Value, $"{fieldName} {header.Key}", errors);
        }
    }

    private static void ValidateRequestOptions(TtsRuleRequestOptionsEditor requestOptions, List<string> errors)
    {
        if (requestOptions.Method is not null && requestOptions.Method is not ("GET" or "POST"))
        {
            errors.Add("requestOptions.method 仅支持 GET 或 POST。");
        }

        if (requestOptions.Method == "GET" && !string.IsNullOrWhiteSpace(requestOptions.Body))
        {
            errors.Add("GET 请求不能携带 body。");
        }

        if (!string.IsNullOrWhiteSpace(requestOptions.Body))
        {
            TryValidateTemplate(requestOptions.Body, "requestOptions.body", errors);
        }
    }

    private static void TryValidateTemplate(string? value, string fieldName, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        try
        {
            NormalizedTemplate.Parse(value);
        }
        catch (FormatException exception)
        {
            errors.Add($"{fieldName} 模板格式无效：{exception.Message}");
        }
    }

    private static IReadOnlyList<TtsRuleEditorKeyValue> ParseKeyValueJson(string? jsonText)
    {
        if (string.IsNullOrWhiteSpace(jsonText))
        {
            return [];
        }

        using var document = JsonDocument.Parse(jsonText);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        return document.RootElement
            .EnumerateObject()
            .Select(property => new TtsRuleEditorKeyValue(
                property.Name,
                property.Value.ValueKind == JsonValueKind.String
                    ? property.Value.GetString() ?? string.Empty
                    : property.Value.GetRawText()))
            .ToArray();
    }

    private static string? SerializeKeyValueJson(IReadOnlyList<TtsRuleEditorKeyValue> entries)
    {
        if (entries.Count == 0)
        {
            return null;
        }

        var dictionary = entries.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase);
        return JsonSerializer.Serialize(dictionary, SerializerOptions);
    }

    private static TtsRuleRequestOptionsEditor ParseRequestOptions(string? requestOptionsJson)
    {
        if (string.IsNullOrWhiteSpace(requestOptionsJson))
        {
            return new TtsRuleRequestOptionsEditor(null, null);
        }

        using var document = JsonDocument.Parse(requestOptionsJson);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return new TtsRuleRequestOptionsEditor(null, null);
        }

        string? method = null;
        string? body = null;

        foreach (var property in document.RootElement.EnumerateObject())
        {
            switch (property.Name)
            {
                case "method":
                    method = property.Value.ValueKind == JsonValueKind.String
                        ? property.Value.GetString()
                        : property.Value.GetRawText();
                    break;
                case "body":
                    body = property.Value.ValueKind == JsonValueKind.String
                        ? property.Value.GetString()
                        : property.Value.GetRawText();
                    break;
            }
        }

        return new TtsRuleRequestOptionsEditor(method, body);
    }

    private static string? SerializeRequestOptions(TtsRuleRequestOptionsEditor requestOptions)
    {
        var hasMethod = !string.IsNullOrWhiteSpace(requestOptions.Method);
        var hasBody = !string.IsNullOrWhiteSpace(requestOptions.Body);

        if (!hasMethod && !hasBody)
        {
            return null;
        }

        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });

        writer.WriteStartObject();
        if (hasMethod)
        {
            writer.WriteString("method", requestOptions.Method);
        }

        if (hasBody)
        {
            writer.WritePropertyName("body");
            WriteJsonLikeValue(writer, requestOptions.Body!);
        }

        writer.WriteEndObject();
        writer.Flush();
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteJsonLikeValue(Utf8JsonWriter writer, string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            document.RootElement.WriteTo(writer);
        }
        catch (JsonException)
        {
            writer.WriteStringValue(text);
        }
    }

    [GeneratedRegex(@"^\d+/\d+$")]
    private static partial Regex ConcurrentRatePattern();
}
