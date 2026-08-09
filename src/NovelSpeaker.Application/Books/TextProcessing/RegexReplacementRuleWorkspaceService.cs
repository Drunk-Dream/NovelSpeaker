using System.Text.Encodings.Web;
using System.Text.Json;
using NovelSpeaker.Domain.Books;
using NovelSpeaker.Application.Books.RuleEditing;

namespace NovelSpeaker.Application.Books.TextProcessing;

/// <summary>Owns regex-rule validation, field-level saves, quick state changes, and ordering.</summary>
public sealed class RegexReplacementRuleWorkspaceService : IRegexReplacementRuleWorkspaceService
{
    private const int SortOrderStep = 10;
    private static readonly TimeSpan RuleTimeout = TimeSpan.FromMilliseconds(100);
    private readonly IRegexReplacementRuleRepository _repository;
    private readonly IRegexReplacementRuleErrorStore _errorStore;
    private readonly TimeProvider _timeProvider;

    public RegexReplacementRuleWorkspaceService(
        IRegexReplacementRuleRepository repository,
        IRegexReplacementRuleErrorStore errorStore,
        TimeProvider timeProvider)
    {
        _repository = repository;
        _errorStore = errorStore;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<RegexReplacementRuleListItem>> GetRulesAsync(
        CancellationToken cancellationToken)
    {
        var rules = await _repository.GetAllAsync(cancellationToken);
        return rules
            .OrderBy(rule => rule.SortOrder)
            .ThenBy(rule => rule.Id)
            .Select(rule => new RegexReplacementRuleListItem(
                rule.Id,
                rule.Name,
                RulePatternValidation.Summarize(rule.Pattern),
                rule.IsEnabled,
                rule.SortOrder,
                rule.Scope,
                GetError(rule)))
            .ToArray();
    }

    public async Task<RegexReplacementRuleEditorModel?> GetEditorAsync(
        Guid ruleId,
        CancellationToken cancellationToken)
    {
        var rule = (await _repository.GetAllAsync(cancellationToken))
            .FirstOrDefault(item => item.Id == ruleId);
        return rule is null ? null : MapEditor(rule);
    }

    public async Task<RegexReplacementRuleEditorModel> SaveEditorAsync(
        RegexReplacementRuleEditorModel editor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(editor);
        var name = RulePatternValidation.NormalizeRequired(editor.Name, "规则名称");
        var pattern = RulePatternValidation.NormalizeRequired(editor.Pattern, "正则表达式");
        RulePatternValidation.Validate(pattern, RuleTimeout);

        var rules = await _repository.GetAllAsync(cancellationToken);
        var existing = editor.Id is Guid id ? rules.FirstOrDefault(item => item.Id == id) : null;
        if (editor.Id is not null && existing is null)
        {
            throw new InvalidOperationException("规则不存在，可能已被删除。");
        }

        var now = _timeProvider.GetUtcNow();
        var saved = new RegexReplacementRule(
            existing?.Id ?? Guid.NewGuid(),
            name,
            existing?.IsEnabled ?? true,
            existing?.SortOrder ?? GetNextSortOrder(rules),
            pattern,
            editor.Replacement ?? string.Empty,
            editor.Scope,
            existing?.CreatedAt ?? now,
            now);
        await _repository.SaveAsync(saved, cancellationToken);
        return MapEditor(saved);
    }

    public Task SetRuleEnabledAsync(Guid ruleId, bool isEnabled, CancellationToken cancellationToken)
    {
        return _repository.UpdateEnabledAsync(ruleId, isEnabled, cancellationToken);
    }

    public async Task<string?> ExportRuleJsonAsync(Guid ruleId, CancellationToken cancellationToken)
    {
        var rule = (await _repository.GetAllAsync(cancellationToken)).FirstOrDefault(item => item.Id == ruleId);
        return rule is null ? null : SerializePortableRule(rule);
    }

    public async Task<RuleJsonImportResult> ImportJsonAsync(
        string json,
        CancellationToken cancellationToken)
    {
        var candidates = ParsePortableRules(json);
        var existing = (await _repository.GetAllAsync(cancellationToken)).ToList();
        var imported = 0;
        var skipped = 0;

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (existing.Any(rule => PortableFieldsEqual(rule, candidate)))
            {
                skipped++;
                continue;
            }

            var now = _timeProvider.GetUtcNow();
            var rule = new RegexReplacementRule(
                Guid.NewGuid(),
                candidate.Name,
                candidate.IsEnabled,
                GetNextSortOrder(existing),
                candidate.Pattern,
                candidate.Replacement,
                candidate.Scope,
                now,
                now);
            await _repository.SaveAsync(rule, cancellationToken);
            existing.Add(rule);
            imported++;
        }

        return new RuleJsonImportResult(imported, skipped, candidates.Count);
    }

    public async Task SaveOrderAsync(
        IReadOnlyList<Guid> orderedRuleIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(orderedRuleIds);
        var rules = await _repository.GetAllAsync(cancellationToken);
        if (orderedRuleIds.Count != rules.Count ||
            orderedRuleIds.Distinct().Count() != rules.Count ||
            rules.Any(rule => !orderedRuleIds.Contains(rule.Id)))
        {
            throw new InvalidOperationException("排序保存失败，请刷新后重试。");
        }

        var order = orderedRuleIds
            .Select((id, index) => (RuleId: id, SortOrder: (index + 1) * SortOrderStep))
            .ToArray();
        await _repository.SaveOrderAsync(order, cancellationToken);
    }

    public Task DeleteRuleAsync(Guid ruleId, CancellationToken cancellationToken)
    {
        return _repository.DeleteAsync(ruleId, cancellationToken);
    }

    private string? GetError(RegexReplacementRule rule)
    {
        if (!RulePatternValidation.IsValid(rule.Pattern, RuleTimeout))
        {
            return "规则格式无效，已隔离。";
        }

        return _errorStore.Current.GetValueOrDefault(rule.Id);
    }

    private static RegexReplacementRuleEditorModel MapEditor(RegexReplacementRule rule)
    {
        return new RegexReplacementRuleEditorModel(
            rule.Id,
            rule.Name,
            rule.Pattern,
            rule.Replacement,
            rule.Scope);
    }

    private static int GetNextSortOrder(IReadOnlyList<RegexReplacementRule> rules)
    {
        return rules.Count == 0 ? SortOrderStep : rules.Max(item => item.SortOrder) + SortOrderStep;
    }

    private static IReadOnlyList<PortableRegexReplacementRule> ParsePortableRules(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidOperationException("规则 JSON 不能为空。");
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var elements = document.RootElement.ValueKind switch
            {
                JsonValueKind.Object => [document.RootElement.Clone()],
                JsonValueKind.Array => document.RootElement.EnumerateArray().Select(element => element.Clone()).ToArray(),
                _ => throw new InvalidOperationException("规则 JSON 必须是单条对象或对象数组。")
            };
            return elements.Select(ParsePortableRule).ToArray();
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("规则 JSON 格式无效。", exception);
        }
    }

    private static PortableRegexReplacementRule ParsePortableRule(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("规则数组只能包含对象。");
        }

        var name = RulePatternValidation.NormalizeRequired(ReadRequiredString(element, "name"), "规则名称");
        var pattern = RulePatternValidation.NormalizeRequired(ReadRequiredString(element, "pattern"), "正则表达式");
        RulePatternValidation.Validate(pattern, RuleTimeout);
        var replacement = ReadOptionalString(element, "replacement") ?? string.Empty;
        var scopeText = ReadRequiredString(element, "scope");
        if (!Enum.TryParse<RegexReplacementScope>(scopeText, true, out var scope) ||
            !Enum.IsDefined(scope))
        {
            throw new InvalidOperationException("字段 scope 必须是 Display、Speech 或 Both。");
        }

        return new PortableRegexReplacementRule(
            name,
            pattern,
            replacement,
            scope,
            ReadBoolean(element, "isEnabled", true));
    }

    private static string SerializePortableRule(RegexReplacementRule rule)
    {
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
        writer.WriteStartObject();
        writer.WriteString("name", rule.Name);
        writer.WriteString("pattern", rule.Pattern);
        writer.WriteString("replacement", rule.Replacement);
        writer.WriteString("scope", rule.Scope.ToString());
        writer.WriteBoolean("isEnabled", rule.IsEnabled);
        writer.WriteEndObject();
        writer.Flush();
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private static bool PortableFieldsEqual(
        RegexReplacementRule rule,
        PortableRegexReplacementRule candidate) =>
        string.Equals(rule.Name, candidate.Name, StringComparison.Ordinal) &&
        string.Equals(rule.Pattern, candidate.Pattern, StringComparison.Ordinal) &&
        string.Equals(rule.Replacement, candidate.Replacement, StringComparison.Ordinal) &&
        rule.Scope == candidate.Scope &&
        rule.IsEnabled == candidate.IsEnabled;

    private static string ReadRequiredString(JsonElement element, string propertyName) =>
        ReadOptionalString(element, propertyName) ?? string.Empty;

    private static string? ReadOptionalString(JsonElement element, string propertyName)
    {
        var property = element.EnumerateObject().FirstOrDefault(candidate =>
            candidate.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase));
        return property.Value.ValueKind switch
        {
            JsonValueKind.String => property.Value.GetString(),
            JsonValueKind.Undefined or JsonValueKind.Null => null,
            _ => throw new InvalidOperationException($"字段 {propertyName} 必须是字符串。")
        };
    }

    private static bool ReadBoolean(JsonElement element, string propertyName, bool defaultValue)
    {
        var property = element.EnumerateObject().FirstOrDefault(candidate =>
            candidate.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase));
        return property.Value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Undefined => defaultValue,
            _ => throw new InvalidOperationException($"字段 {propertyName} 必须是布尔值。")
        };
    }

    private sealed record PortableRegexReplacementRule(
        string Name,
        string Pattern,
        string Replacement,
        RegexReplacementScope Scope,
        bool IsEnabled);
}
