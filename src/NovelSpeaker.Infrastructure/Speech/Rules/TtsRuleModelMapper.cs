using System.Text.Json;
using System.Text.RegularExpressions;
using NovelSpeaker.Application.Speech;
using NovelSpeaker.Application.Speech.Compilation;
using NovelSpeaker.Application.Speech.Rules;
using NovelSpeaker.Domain.Speech;

namespace NovelSpeaker.Infrastructure.Speech.Rules;

internal static partial class TtsRuleModelMapper
{
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
            rule.Headers.Select(pair => new TtsRuleEditorKeyValue(pair.Key, pair.Value)).ToArray(),
            new TtsRuleRequestOptionsEditor(rule.RequestMethod, rule.RequestBody));
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

    public static HttpTtsRule BuildRuleFromEditor(
        TtsRuleEditorModel editor,
        HttpTtsRule? existingRule,
        TimeProvider? timeProvider = null)
    {
        var utcNow = (timeProvider ?? TimeProvider.System).GetUtcNow();

        var body = ToPersistedBody(editor.RequestOptions.Body);
        return new HttpTtsRule(
            editor.Id ?? 0,
            editor.Name,
            editor.Url,
            editor.ContentType,
            editor.ConcurrentRate,
            editor.Headers.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase),
            editor.RequestOptions.Method,
            body.Text,
            body.IsJsonStructure,
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

    private static PersistedBody ToPersistedBody(string? editorBody)
    {
        if (string.IsNullOrWhiteSpace(editorBody))
        {
            return new PersistedBody(null, false);
        }

        try
        {
            using var document = JsonDocument.Parse(editorBody);
            return document.RootElement.ValueKind == JsonValueKind.String
                ? new PersistedBody(document.RootElement.GetString(), false)
                : new PersistedBody(document.RootElement.GetRawText(), true);
        }
        catch (JsonException)
        {
            return new PersistedBody(editorBody, false);
        }
    }

    private sealed record PersistedBody(string? Text, bool IsJsonStructure);

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

    [GeneratedRegex(@"^\d+/\d+$")]
    private static partial Regex ConcurrentRatePattern();
}
