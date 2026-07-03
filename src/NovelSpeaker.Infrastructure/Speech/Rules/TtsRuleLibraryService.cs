using System.Text.Json;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Application.Speech;
using NovelSpeaker.Domain.Settings;
using NovelSpeaker.Domain.Speech;

namespace NovelSpeaker.Infrastructure.Speech.Rules;

/// <summary>
/// Handles import, selection, export, and projection of persisted HTTP TTS rules.
/// </summary>
public sealed class TtsRuleLibraryService : ITtsRuleLibraryService
{
    private readonly ITtsRuleConverter _ruleConverter;
    private static readonly JsonSerializerOptions SerializerOptions = new();

    public async Task<IReadOnlyList<TtsRuleSummary>> GetRulesAsync(CancellationToken cancellationToken)
    {
        var rules = await _repository.GetAllAsync(cancellationToken);
        var settings = await _settingsService.LoadAsync(cancellationToken);

        return rules.Select(rule => ToSummary(rule, settings.SelectedTtsRuleId)).ToArray();
    }

    public async Task<TtsRuleImportPreview> CreateImportPreviewAsync(
        string jsonText,
        string sourceDescription,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(jsonText))
        {
            return new TtsRuleImportPreview(sourceDescription, [], "没有可导入的 JSON 内容。");
        }

        try
        {
            using var document = JsonDocument.Parse(jsonText);
            var root = document.RootElement;
            if (root.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
            {
                return new TtsRuleImportPreview(sourceDescription, [], "规则 JSON 必须是对象或对象数组。");
            }

            var existingRules = await _repository.GetAllAsync(cancellationToken);
            return new TtsRuleImportPreview(sourceDescription, CreateImportItems(root, existingRules), null);
        }
        catch (JsonException)
        {
            return new TtsRuleImportPreview(sourceDescription, [], "JSON 解析失败，请检查规则内容。");
        }
    }

    public async Task<TtsRuleImportResult> ImportJsonTextAsync(
        string jsonText,
        string sourceDescription,
        CancellationToken cancellationToken)
    {
        var preview = await CreateImportPreviewAsync(jsonText, sourceDescription, cancellationToken);
        return await ImportAsync(preview, cancellationToken);
    }

    public async Task<TtsRuleImportResult> ImportAsync(TtsRuleImportPreview preview, CancellationToken cancellationToken)
    {
        if (preview.ErrorMessage is not null)
        {
            return new TtsRuleImportResult(0, 0, preview.Items.Count)
            {
                FailedCount = preview.Items.Count
            };
        }

        var importedCount = 0;
        var skippedCount = 0;
        var failedCount = 0;
        long? firstImportedRuleId = null;
        long? firstImportedEnabledRuleId = null;
        var existingRules = (await _repository.GetAllAsync(cancellationToken)).ToList();

        foreach (var item in preview.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (IsExactDuplicate(existingRules, item))
            {
                skippedCount++;
                continue;
            }

            if (!item.CanImport)
            {
                failedCount++;
                continue;
            }

            var savedRule = await SaveImportedRuleAsync(item.CandidateRule, existingRules, cancellationToken);
            firstImportedRuleId ??= savedRule.Id;
            if (savedRule.IsEnabled)
            {
                firstImportedEnabledRuleId ??= savedRule.Id;
            }

            existingRules.Add(savedRule);
            importedCount++;
        }

        if (firstImportedEnabledRuleId is not null)
        {
            var settings = await _settingsService.LoadAsync(cancellationToken);
            if (settings.SelectedTtsRuleId is null)
            {
                await SelectRuleAsync(firstImportedEnabledRuleId.Value, cancellationToken);
            }
        }

        return new TtsRuleImportResult(importedCount, skippedCount, preview.Items.Count)
        {
            FailedCount = failedCount,
            FirstImportedRuleId = firstImportedRuleId
        };
    }

    public async Task<string?> ExportRuleJsonAsync(long ruleId, CancellationToken cancellationToken)
    {
        var rule = await _repository.GetByIdAsync(ruleId, cancellationToken);
        return rule?.RuleJson;
    }

    public async Task<TtsRuleEditorModel?> GetEditorAsync(long ruleId, CancellationToken cancellationToken)
    {
        var rule = await _repository.GetByIdAsync(ruleId, cancellationToken);
        return rule is null ? null : ToEditor(rule);
    }

    public async Task<TtsRuleValidationResult> ValidateEditorAsync(
        TtsRuleEditorModel editor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(editor);

        var existingRule = editor.Id is > 0
            ? await _repository.GetByIdAsync(editor.Id.Value, cancellationToken)
            : null;

        var errors = new List<string>();
        var normalizedEditor = NormalizeEditor(editor);

        ValidateNameAndUrl(normalizedEditor, errors);
        ValidateHeaders(normalizedEditor.Headers, "Header", errors);
        ValidateHeaders(normalizedEditor.LoginInfo, "LoginInfo", errors);
        ValidateRequestOptions(normalizedEditor.RequestOptions, errors);

        var normalizedRule = BuildRuleFromEditor(normalizedEditor, existingRule);
        normalizedRule = normalizedRule with
        {
            RuleJson = NovelSpeakerRuleJsonSerializer.Serialize(normalizedRule)
        };

        if (!string.IsNullOrWhiteSpace(normalizedEditor.RawRuleJson))
        {
            try
            {
                var canonicalRawJson = CanonicalizeRawRuleJson(normalizedEditor.RawRuleJson, normalizedRule);
                if (!string.Equals(canonicalRawJson, normalizedRule.RuleJson, StringComparison.Ordinal))
                {
                    errors.Add("原始 JSON 与结构化字段不一致。");
                }
            }
            catch (JsonException exception)
            {
                errors.Add($"原始 JSON 不是有效的 JSON 对象：{exception.Message}");
            }
            catch (InvalidOperationException exception)
            {
                errors.Add(exception.Message);
            }
        }

        normalizedEditor = normalizedEditor with
        {
            RawRuleJson = normalizedRule.RuleJson
        };

        return new TtsRuleValidationResult(errors.Count == 0, errors, normalizedEditor);
    }

    public async Task<HttpTtsRule> SaveEditorAsync(TtsRuleEditorModel editor, CancellationToken cancellationToken)
    {
        var validation = await ValidateEditorAsync(editor, cancellationToken);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException(string.Join(" ", validation.Errors));
        }

        var existingRule = validation.NormalizedModel.Id is > 0
            ? await _repository.GetByIdAsync(validation.NormalizedModel.Id.Value, cancellationToken)
            : null;
        var rule = BuildRuleFromEditor(validation.NormalizedModel, existingRule);
        var existingRules = await _repository.GetAllAsync(cancellationToken);
        rule = EnsureUniqueRuleName(rule, existingRules, existingRule?.Id);
        rule = rule with { RuleJson = NovelSpeakerRuleJsonSerializer.Serialize(rule) };

        var ruleId = await _repository.SaveAsync(rule, cancellationToken);
        var savedRule = (await _repository.GetByIdAsync(ruleId, cancellationToken))!;

        if (existingRule is null && savedRule.IsEnabled)
        {
            var settings = await _settingsService.LoadAsync(cancellationToken);
            if (settings.SelectedTtsRuleId is null)
            {
                await SelectRuleAsync(savedRule.Id, cancellationToken);
                savedRule = (await _repository.GetByIdAsync(ruleId, cancellationToken))!;
            }
        }

        return savedRule;
    }

    public async Task<TtsRuleProtectionInfo> GetRuleProtectionAsync(
        long ruleId,
        TtsRuleMutationAction action,
        CancellationToken cancellationToken)
    {
        var settings = await _settingsService.LoadAsync(cancellationToken);
        var isCurrentRule = settings.SelectedTtsRuleId == ruleId;
        var rules = await GetRulesAsync(cancellationToken);
        var replacementCandidates = rules
            .Where(rule => rule.Id != ruleId && rule.IsEnabled)
            .ToArray();

        return new TtsRuleProtectionInfo(
            ruleId,
            action,
            isCurrentRule,
            !isCurrentRule,
            isCurrentRule,
            replacementCandidates);
    }

    public async Task<TtsRuleMutationResult> ApplyRuleMutationAsync(
        TtsRuleMutationDecision decision,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(decision);

        var protection = await GetRuleProtectionAsync(decision.RuleId, decision.Action, cancellationToken);
        if (protection.IsCurrentRule && !protection.CanApplyDirectly)
        {
            if (decision.ReplacementRuleId is not null)
            {
                var replacementRule = await _repository.GetByIdAsync(decision.ReplacementRuleId.Value, cancellationToken);
                if (replacementRule is null || !replacementRule.IsEnabled || replacementRule.Id == decision.RuleId)
                {
                    throw new InvalidOperationException("必须选择另一条已启用规则作为替代规则。");
                }
            }
            else if (!decision.ClearSelectedRule || !protection.CanClearSelectedRule)
            {
                throw new InvalidOperationException("当前规则需要明确清空当前规则后才能继续。");
            }
        }

        switch (decision.Action)
        {
            case TtsRuleMutationAction.Disable:
                {
                    var rule = await _repository.GetByIdAsync(decision.RuleId, cancellationToken);
                    if (rule is null)
                    {
                        throw new InvalidOperationException("未找到要禁用的规则。");
                    }

                    var utcNow = DateTime.UtcNow.ToString("O");
                    await _repository.SaveAsync(rule with
                    {
                        IsEnabled = false,
                        UpdatedAt = utcNow
                    }, cancellationToken);
                    break;
                }
            case TtsRuleMutationAction.Delete:
                await _repository.DeleteAsync(decision.RuleId, cancellationToken);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(decision), decision.Action, null);
        }

        if (decision.ReplacementRuleId is not null)
        {
            await SelectRuleAsync(decision.ReplacementRuleId, cancellationToken);
            return new TtsRuleMutationResult(
                decision.RuleId,
                decision.Action,
                decision.ReplacementRuleId,
                false);
        }

        if (protection.IsCurrentRule)
        {
            await UpdateSelectedRuleAsync(null, cancellationToken);
        }

        var settings = await _settingsService.LoadAsync(cancellationToken);
        return new TtsRuleMutationResult(
            decision.RuleId,
            decision.Action,
            settings.SelectedTtsRuleId,
            protection.IsCurrentRule && settings.SelectedTtsRuleId is null);
    }

    public async Task SelectRuleAsync(long? ruleId, CancellationToken cancellationToken)
    {
        if (ruleId is null)
        {
            await UpdateSelectedRuleAsync(null, cancellationToken);
            return;
        }

        var rule = await _repository.GetByIdAsync(ruleId.Value, cancellationToken);
        if (rule is null || !rule.IsEnabled)
        {
            throw new InvalidOperationException("只能将存在且已启用的规则设为当前规则。");
        }

        var utcNow = DateTime.UtcNow.ToString("O");
        await _repository.SaveAsync(rule with { LastUsedAt = utcNow, UpdatedAt = utcNow }, cancellationToken);

        await UpdateSelectedRuleAsync(rule.Id, cancellationToken);
    }

    public async Task SetRuleEnabledAsync(long ruleId, bool isEnabled, CancellationToken cancellationToken)
    {
        var rule = await _repository.GetByIdAsync(ruleId, cancellationToken);
        if (rule is null)
        {
            return;
        }

        var utcNow = DateTime.UtcNow.ToString("O");
        await _repository.SaveAsync(rule with
        {
            IsEnabled = isEnabled,
            UpdatedAt = utcNow
        }, cancellationToken);

        if (isEnabled)
        {
            return;
        }

        await ClearSelectedRuleIfNeededAsync(ruleId, cancellationToken);
    }

    public async Task DeleteRuleAsync(long ruleId, CancellationToken cancellationToken)
    {
        await _repository.DeleteAsync(ruleId, cancellationToken);

        await ClearSelectedRuleIfNeededAsync(ruleId, cancellationToken);
    }

    private readonly ITtsRuleRepository _repository;
    private readonly IAppSettingsService _settingsService;

    public TtsRuleLibraryService(
        ITtsRuleRepository repository,
        IAppSettingsService settingsService,
        ITtsRuleConverter ruleConverter)
    {
        _repository = repository;
        _settingsService = settingsService;
        _ruleConverter = ruleConverter;
    }

    private TtsRuleImportItem CreateImportItem(JsonElement element, int index, IReadOnlyList<HttpTtsRule> existingRules)
    {
        var conversion = _ruleConverter.Convert(element);
        var candidateRule = conversion.CandidateRule;
        var ruleJson = candidateRule.RuleJson;
        var exactDuplicate = existingRules.Any(rule => string.Equals(rule.RuleJson, ruleJson, StringComparison.Ordinal));
        var sameNameConflict = !string.IsNullOrWhiteSpace(candidateRule.Name) &&
                               existingRules.Any(rule => string.Equals(rule.Name, candidateRule.Name, StringComparison.OrdinalIgnoreCase)) &&
                               !exactDuplicate;
        var canImport = conversion.CanImport && !exactDuplicate;
        var statusMessage = BuildStatusMessage(conversion, exactDuplicate, sameNameConflict);

        return new TtsRuleImportItem(
            index,
            string.IsNullOrWhiteSpace(candidateRule.Name) ? $"未命名规则 #{index + 1}" : candidateRule.Name,
            candidateRule.Url,
            conversion.CompatibilityStatus,
            conversion.UnsupportedFields,
            canImport,
            exactDuplicate,
            sameNameConflict,
            statusMessage,
            candidateRule with
            {
                CompatibilityStatus = conversion.CompatibilityStatus,
                UnsupportedFields = conversion.UnsupportedFields
            });
    }

    private static TtsRuleImportItem CreateInvalidItem(int index, string statusMessage)
    {
        var utcNow = DateTime.UtcNow.ToString("O");

        return new TtsRuleImportItem(
            index,
            $"无效规则 #{index + 1}",
            string.Empty,
            TtsRuleCompatibilityStatus.NeedsManualAdjustment,
            [],
            false,
            false,
            false,
            statusMessage,
            new HttpTtsRule(
                0,
                string.Empty,
                string.Empty,
                null,
                null,
                null,
                null,
                false,
                null,
                "{}",
                false,
                TtsRuleCompatibilityStatus.NeedsManualAdjustment,
                [],
                null,
                utcNow,
                utcNow));
    }

    private static string BuildStatusMessage(
        TtsRuleConversionResult conversion,
        bool exactDuplicate,
        bool sameNameConflict)
    {
        if (exactDuplicate)
        {
            return "与现有规则完全相同，将跳过导入。";
        }

        if (sameNameConflict)
        {
            return "名称与现有规则重复，但内容不同，将作为新规则导入。";
        }

        if (conversion.BlockingIssues.Count > 0)
        {
            return string.Join(" ", conversion.BlockingIssues);
        }

        return conversion.CompatibilityStatus switch
        {
            TtsRuleCompatibilityStatus.Compatible => "可直接导入。",
            TtsRuleCompatibilityStatus.CompatibleWithWarnings => $"可导入，但包含未支持字段：{string.Join("、", conversion.UnsupportedFields)}。",
            _ => "当前规则无法转换为本应用规则。"
        };
    }

    private static TtsRuleSummary ToSummary(HttpTtsRule rule, long? selectedRuleId)
    {
        return new TtsRuleSummary(
            rule.Id,
            rule.Name,
            rule.IsEnabled,
            selectedRuleId == rule.Id && rule.IsEnabled,
            rule.LastUsedAt,
            rule.CompatibilityStatus,
            rule.UnsupportedFields);
    }

    private static TtsRuleEditorModel ToEditor(HttpTtsRule rule)
    {
        return new TtsRuleEditorModel(
            rule.Id,
            rule.Name,
            rule.IsEnabled,
            rule.Url,
            rule.ContentType,
            rule.ConcurrentRate,
            rule.EnabledCookieJar,
            rule.LastUpdateTime,
            ParseKeyValueJson(rule.Header),
            ParseRequestOptions(rule.RequestOptionsJson),
            rule.RuleJson,
            rule.CompatibilityStatus,
            rule.UnsupportedFields)
        {
            LoginInfo = ParseKeyValueJson(rule.LoginInfoJson)
        };
    }

    private static TtsRuleEditorModel NormalizeEditor(TtsRuleEditorModel editor)
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
            LoginInfo = editor.LoginInfo
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Key))
                .Select(entry => new TtsRuleEditorKeyValue(entry.Key.Trim(), entry.Value))
                .ToArray(),
            RequestOptions = editor.RequestOptions with
            {
                Method = NormalizeOptionalText(editor.RequestOptions.Method)?.ToUpperInvariant(),
                Body = NormalizeOptionalText(editor.RequestOptions.Body),
                Headers = editor.RequestOptions.Headers
                    .Where(entry => !string.IsNullOrWhiteSpace(entry.Key))
                    .Select(entry => new TtsRuleEditorKeyValue(entry.Key.Trim(), entry.Value))
                    .ToArray()
            },
            RawRuleJson = editor.RawRuleJson.Trim()
        };
    }

    private static string? NormalizeOptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static void ValidateNameAndUrl(TtsRuleEditorModel editor, List<string> errors)
    {
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
            !System.Text.RegularExpressions.Regex.IsMatch(editor.ConcurrentRate, @"^\d+/\d+$"))
        {
            errors.Add("并发限制必须使用类似 2/1000 的格式。");
        }
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
        ValidateHeaders(requestOptions.Headers, "requestOptions.headers", errors);

        if (requestOptions.Method is not null && requestOptions.Method is not ("GET" or "POST"))
        {
            errors.Add("requestOptions.method 仅支持 GET 或 POST。");
        }

        if (requestOptions.TimeoutMs is <= 0 or > 300000)
        {
            errors.Add("requestOptions.timeoutMs 必须在 1 到 300000 之间。");
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

    private static HttpTtsRule BuildRuleFromEditor(TtsRuleEditorModel editor, HttpTtsRule? existingRule)
    {
        var headerJson = SerializeKeyValueJson(editor.Headers);
        var loginInfoJson = SerializeKeyValueJson(editor.LoginInfo);
        var requestOptionsJson = SerializeRequestOptions(editor.RequestOptions);
        var utcNow = DateTime.UtcNow.ToString("O");

        var rule = new HttpTtsRule(
            editor.Id ?? 0,
            editor.Name,
            editor.Url,
            editor.ContentType,
            editor.ConcurrentRate,
            headerJson,
            requestOptionsJson,
            editor.EnabledCookieJar,
            editor.LastUpdateTime,
            string.Empty,
            editor.IsEnabled,
            editor.CompatibilityStatus,
            editor.UnsupportedFields,
            existingRule?.LastUsedAt,
            existingRule?.CreatedAt ?? utcNow,
            utcNow)
        {
            LoginInfoJson = loginInfoJson
        };

        return rule with
        {
            RuleJson = NovelSpeakerRuleJsonSerializer.Serialize(rule)
        };
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
            return new TtsRuleRequestOptionsEditor(null, [], null, null);
        }

        using var document = JsonDocument.Parse(requestOptionsJson);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return new TtsRuleRequestOptionsEditor(null, [], null, null);
        }

        string? method = null;
        List<TtsRuleEditorKeyValue> headers = [];
        string? body = null;
        int? timeoutMs = null;

        foreach (var property in document.RootElement.EnumerateObject())
        {
            switch (property.Name)
            {
                case "method":
                    method = property.Value.ValueKind == JsonValueKind.String
                        ? property.Value.GetString()
                        : property.Value.GetRawText();
                    break;
                case "headers":
                    headers = ParseHeadersElement(property.Value).ToList();
                    break;
                case "body":
                    body = property.Value.ValueKind == JsonValueKind.String
                        ? property.Value.GetString()
                        : property.Value.GetRawText();
                    break;
                case "timeoutMs":
                    timeoutMs = property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out var parsed)
                        ? parsed
                        : property.Value.ValueKind == JsonValueKind.String && int.TryParse(property.Value.GetString(), out parsed)
                            ? parsed
                            : null;
                    break;
            }
        }

        return new TtsRuleRequestOptionsEditor(method, headers, body, timeoutMs);
    }

    private static IReadOnlyList<TtsRuleEditorKeyValue> ParseHeadersElement(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.Object => value.EnumerateObject()
                .Select(property => new TtsRuleEditorKeyValue(
                    property.Name,
                    property.Value.ValueKind == JsonValueKind.String
                        ? property.Value.GetString() ?? string.Empty
                        : property.Value.GetRawText()))
                .ToArray(),
            JsonValueKind.String => ParseKeyValueJson(value.GetString()),
            _ => []
        };
    }

    private static string? SerializeRequestOptions(TtsRuleRequestOptionsEditor requestOptions)
    {
        var hasMethod = !string.IsNullOrWhiteSpace(requestOptions.Method);
        var hasHeaders = requestOptions.Headers.Count > 0;
        var hasBody = !string.IsNullOrWhiteSpace(requestOptions.Body);
        var hasTimeout = requestOptions.TimeoutMs is not null;

        if (!hasMethod && !hasHeaders && !hasBody && !hasTimeout)
        {
            return null;
        }

        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);

        writer.WriteStartObject();
        if (hasMethod)
        {
            writer.WriteString("method", requestOptions.Method);
        }

        if (hasHeaders)
        {
            writer.WritePropertyName("headers");
            writer.WriteStartObject();
            foreach (var header in requestOptions.Headers)
            {
                writer.WriteString(header.Key, header.Value);
            }

            writer.WriteEndObject();
        }

        if (hasBody)
        {
            writer.WritePropertyName("body");
            WriteJsonLikeValue(writer, requestOptions.Body!);
        }

        if (hasTimeout)
        {
            writer.WriteNumber("timeoutMs", requestOptions.TimeoutMs!.Value);
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

    private static string CanonicalizeRawRuleJson(string rawRuleJson, HttpTtsRule baselineRule)
    {
        using var document = JsonDocument.Parse(rawRuleJson);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("原始 JSON 必须是对象。");
        }

        var root = document.RootElement;
        var rawRule = new HttpTtsRule(
            baselineRule.Id,
            ReadOptionalString(root, "name") ?? string.Empty,
            ReadOptionalString(root, "url") ?? string.Empty,
            ReadOptionalString(root, "contentType"),
            ReadOptionalString(root, "concurrentRate"),
            ReadJsonOrString(root, "header"),
            ReadOptionalJson(root, "requestOptions"),
            ReadOptionalBoolean(root, "enabledCookieJar"),
            ReadOptionalInt64(root, "lastUpdateTime"),
            string.Empty,
            baselineRule.IsEnabled,
            baselineRule.CompatibilityStatus,
            baselineRule.UnsupportedFields,
            baselineRule.LastUsedAt,
            baselineRule.CreatedAt,
            baselineRule.UpdatedAt)
        {
            LoginInfoJson = ReadJsonOrString(root, "loginInfo")
        };

        return NovelSpeakerRuleJsonSerializer.Serialize(rawRule);
    }

    private static string? ReadOptionalString(JsonElement root, string propertyName)
    {
        return TryGetProperty(root, propertyName, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.Null => null,
                JsonValueKind.String => value.GetString(),
                JsonValueKind.True => bool.TrueString,
                JsonValueKind.False => bool.FalseString,
                _ => value.GetRawText()
            }
            : null;
    }

    private static string? ReadJsonOrString(JsonElement root, string propertyName)
    {
        if (!TryGetProperty(root, propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Object => value.GetRawText(),
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Null => null,
            _ => value.GetRawText()
        };
    }

    private static string? ReadOptionalJson(JsonElement root, string propertyName)
    {
        return TryGetProperty(root, propertyName, out var value)
            ? value.GetRawText()
            : null;
    }

    private static bool ReadOptionalBoolean(JsonElement root, string propertyName)
    {
        if (!TryGetProperty(root, propertyName, out var value))
        {
            return false;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) => parsed,
            _ => false
        };
    }

    private static long? ReadOptionalInt64(JsonElement root, string propertyName)
    {
        if (!TryGetProperty(root, propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var parsedNumber))
        {
            return parsedNumber;
        }

        return value.ValueKind == JsonValueKind.String &&
               long.TryParse(value.GetString(), out var parsedString)
            ? parsedString
            : null;
    }

    private static bool TryGetProperty(JsonElement root, string propertyName, out JsonElement value)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private List<TtsRuleImportItem> CreateImportItems(JsonElement root, IReadOnlyList<HttpTtsRule> existingRules)
    {
        var items = new List<TtsRuleImportItem>();
        var index = 0;

        if (root.ValueKind == JsonValueKind.Object)
        {
            items.Add(CreateImportItem(root, index, existingRules));
            return items;
        }

        foreach (var element in root.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                items.Add(CreateInvalidItem(index, "规则数组中的每一项都必须是对象。"));
                index++;
                continue;
            }

            items.Add(CreateImportItem(element, index, existingRules));
            index++;
        }

        return items;
    }

    private static bool IsExactDuplicate(IReadOnlyList<HttpTtsRule> existingRules, TtsRuleImportItem item)
    {
        return existingRules.Any(rule => string.Equals(rule.RuleJson, item.CandidateRule.RuleJson, StringComparison.Ordinal));
    }

    private async Task<HttpTtsRule> SaveImportedRuleAsync(
        HttpTtsRule rule,
        IReadOnlyList<HttpTtsRule> existingRules,
        CancellationToken cancellationToken)
    {
        var utcNow = DateTime.UtcNow.ToString("O");
        var normalizedRule = EnsureUniqueRuleName(rule, existingRules, null);
        normalizedRule = normalizedRule with
        {
            RuleJson = NovelSpeakerRuleJsonSerializer.Serialize(normalizedRule),
            CreatedAt = utcNow,
            UpdatedAt = utcNow
        };

        var ruleId = await _repository.SaveAsync(normalizedRule, cancellationToken);
        return normalizedRule with { Id = ruleId };
    }

    private static HttpTtsRule EnsureUniqueRuleName(
        HttpTtsRule rule,
        IReadOnlyList<HttpTtsRule> existingRules,
        long? currentRuleId)
    {
        var baseName = string.IsNullOrWhiteSpace(rule.Name) ? "新建规则" : rule.Name.Trim();
        if (!existingRules.Any(existing =>
                existing.Id != currentRuleId &&
                string.Equals(existing.Name, baseName, StringComparison.OrdinalIgnoreCase)))
        {
            return rule with { Name = baseName };
        }

        var suffix = 2;
        while (true)
        {
            var candidateName = $"{baseName} ({suffix})";
            if (!existingRules.Any(existing =>
                    existing.Id != currentRuleId &&
                    string.Equals(existing.Name, candidateName, StringComparison.OrdinalIgnoreCase)))
            {
                return rule with { Name = candidateName };
            }

            suffix++;
        }
    }

    private async Task UpdateSelectedRuleAsync(long? ruleId, CancellationToken cancellationToken)
    {
        await _settingsService.UpdateAsync(
            new AppSettingsUpdate
            {
                SelectedTtsRuleId = ruleId,
                ClearSelectedTtsRuleId = ruleId is null
            },
            cancellationToken);
    }

    private async Task ClearSelectedRuleIfNeededAsync(long ruleId, CancellationToken cancellationToken)
    {
        var settings = await _settingsService.LoadAsync(cancellationToken);
        if (settings.SelectedTtsRuleId == ruleId)
        {
            await _settingsService.UpdateAsync(
                new AppSettingsUpdate
                {
                    ClearSelectedTtsRuleId = true
                },
                cancellationToken);
        }
    }
}
