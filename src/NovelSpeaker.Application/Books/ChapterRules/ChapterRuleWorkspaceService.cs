using System.Text.Encodings.Web;
using System.Text.Json;
using NovelSpeaker.Domain.Books;
using NovelSpeaker.Application.Books.RuleEditing;

namespace NovelSpeaker.Application.Books.ChapterRules;

/// <summary>Owns chapter-rule editing, quick state changes, and stable ordering.</summary>
public sealed class ChapterRuleWorkspaceService : IChapterRuleWorkspaceService
{
    private const int SortOrderStep = 10;
    private readonly IChapterRuleRepository _repository;
    private readonly IChapterRuleManagementService _managementService;
    private readonly TimeProvider _timeProvider;

    public ChapterRuleWorkspaceService(
        IChapterRuleRepository repository,
        IChapterRuleManagementService managementService,
        TimeProvider timeProvider)
    {
        _repository = repository;
        _managementService = managementService;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<ChapterRuleListItem>> GetRulesAsync(CancellationToken cancellationToken)
    {
        var rules = await _repository.GetAllAsync(cancellationToken);
        return rules
            .OrderBy(rule => rule.SortOrder)
            .ThenBy(rule => rule.Name, StringComparer.Ordinal)
            .Select(MapListItem)
            .ToArray();
    }

    public async Task<ChapterRuleEditorModel?> GetEditorAsync(string ruleId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleId);
        var rule = (await _repository.GetAllAsync(cancellationToken)).FirstOrDefault(candidate =>
            string.Equals(candidate.Id, ruleId, StringComparison.Ordinal));
        return rule is null ? null : MapEditor(rule);
    }

    public async Task<ChapterRuleEditorModel> SaveEditorAsync(
        ChapterRuleEditorModel editor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(editor);
        var normalizedName = RulePatternValidation.NormalizeRequired(editor.Name, "规则名称");
        var normalizedPattern = RulePatternValidation.NormalizeRequired(editor.Pattern, "正则表达式");
        RulePatternValidation.Validate(normalizedPattern);

        var allRules = await _repository.GetAllAsync(cancellationToken);
        var existing = editor.Id is null
            ? null
            : allRules.FirstOrDefault(rule => string.Equals(rule.Id, editor.Id, StringComparison.Ordinal));
        if (editor.Id is not null && existing is null)
        {
            throw new InvalidOperationException("章节规则不存在，可能已被删除。");
        }

        var id = existing?.Id ?? $"custom:{Guid.NewGuid():N}";
        var utcNow = _timeProvider.GetUtcNow();
        var savedRule = new ChapterRule(
            id,
            DeduplicateName(normalizedName, id, allRules),
            normalizedPattern,
            existing?.SortOrder ?? GetNextSortOrder(allRules),
            existing?.IsEnabled ?? true,
            existing?.CreatedAt ?? utcNow,
            utcNow);

        await _repository.SaveAsync(savedRule, cancellationToken);
        return MapEditor(savedRule);
    }

    public Task DeleteRuleAsync(string ruleId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleId);
        if (DefaultChapterRules.IsBuiltInId(ruleId))
        {
            throw new InvalidOperationException("内置规则不可删除。");
        }

        return _repository.DeleteAsync(ruleId, cancellationToken);
    }

    public async Task SetRuleEnabledAsync(string ruleId, bool isEnabled, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleId);
        var rule = (await _repository.GetAllAsync(cancellationToken)).FirstOrDefault(candidate =>
            string.Equals(candidate.Id, ruleId, StringComparison.Ordinal));
        if (rule is null)
        {
            throw new InvalidOperationException("章节规则不存在，可能已被删除。");
        }

        await _repository.SaveAsync(rule with
        {
            IsEnabled = isEnabled,
            UpdatedAt = _timeProvider.GetUtcNow()
        }, cancellationToken);
    }

    public async Task<string?> ExportRuleJsonAsync(string ruleId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleId);
        var rule = (await _repository.GetAllAsync(cancellationToken)).FirstOrDefault(candidate =>
            string.Equals(candidate.Id, ruleId, StringComparison.Ordinal));
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
            var rule = new ChapterRule(
                $"custom:{Guid.NewGuid():N}",
                candidate.Name,
                candidate.Pattern,
                GetNextSortOrder(existing),
                candidate.IsEnabled,
                now,
                now);
            await _repository.SaveAsync(rule, cancellationToken);
            existing.Add(rule);
            imported++;
        }

        return new RuleJsonImportResult(imported, skipped, candidates.Count);
    }

    public async Task SaveOrderAsync(IReadOnlyList<string> orderedRuleIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(orderedRuleIds);
        var rules = await _repository.GetAllAsync(cancellationToken);
        var byId = rules.ToDictionary(rule => rule.Id, StringComparer.Ordinal);
        if (orderedRuleIds.Count != rules.Count ||
            orderedRuleIds.Any(ruleId => !byId.ContainsKey(ruleId)) ||
            orderedRuleIds.Distinct(StringComparer.Ordinal).Count() != orderedRuleIds.Count)
        {
            throw new InvalidOperationException("排序保存失败，请刷新后重试。");
        }

        var order = orderedRuleIds
            .Select((ruleId, index) => (RuleId: ruleId, SortOrder: (index + 1) * SortOrderStep))
            .ToArray();
        await _repository.SaveOrderAsync(order, cancellationToken);
    }

    public Task<ChapterRuleDefaultsApplyResult> ApplyDefaultsAsync(
        ChapterRuleDefaultsMode mode,
        CancellationToken cancellationToken)
    {
        return _managementService.ApplyDefaultsAsync(mode, cancellationToken);
    }

    private static ChapterRuleListItem MapListItem(ChapterRule rule)
    {
        var isBuiltIn = DefaultChapterRules.IsBuiltInId(rule.Id);
        return new ChapterRuleListItem(
            rule.Id,
            rule.Name,
            RulePatternValidation.Summarize(rule.Pattern),
            rule.IsEnabled,
            rule.SortOrder,
            isBuiltIn,
            !isBuiltIn);
    }

    private static ChapterRuleEditorModel MapEditor(ChapterRule rule)
    {
        var isBuiltIn = DefaultChapterRules.IsBuiltInId(rule.Id);
        return new ChapterRuleEditorModel(rule.Id, rule.Name, rule.Pattern, isBuiltIn, !isBuiltIn);
    }

    private static int GetNextSortOrder(IReadOnlyList<ChapterRule> allRules)
    {
        return allRules.Count == 0 ? SortOrderStep : allRules.Max(rule => rule.SortOrder) + SortOrderStep;
    }

    private static IReadOnlyList<PortableChapterRule> ParsePortableRules(string json)
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
            var rules = elements.Select(ParsePortableRule).ToArray();
            return rules;
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("规则 JSON 格式无效。", exception);
        }
    }

    private static PortableChapterRule ParsePortableRule(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("规则数组只能包含对象。");
        }

        var name = RulePatternValidation.NormalizeRequired(ReadRequiredString(element, "name"), "规则名称");
        var pattern = RulePatternValidation.NormalizeRequired(ReadRequiredString(element, "pattern"), "正则表达式");
        RulePatternValidation.Validate(pattern);
        return new PortableChapterRule(name, pattern, ReadBoolean(element, "isEnabled", true));
    }

    private static string SerializePortableRule(ChapterRule rule)
    {
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
        writer.WriteStartObject();
        writer.WriteString("name", rule.Name);
        writer.WriteString("pattern", rule.Pattern);
        writer.WriteBoolean("isEnabled", rule.IsEnabled);
        writer.WriteEndObject();
        writer.Flush();
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private static bool PortableFieldsEqual(ChapterRule rule, PortableChapterRule candidate) =>
        string.Equals(rule.Name, candidate.Name, StringComparison.Ordinal) &&
        string.Equals(rule.Pattern, candidate.Pattern, StringComparison.Ordinal) &&
        rule.IsEnabled == candidate.IsEnabled;

    private static string ReadRequiredString(JsonElement element, string propertyName)
    {
        var property = element.EnumerateObject().FirstOrDefault(candidate =>
            candidate.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase));
        return property.Value.ValueKind == JsonValueKind.String
            ? property.Value.GetString() ?? string.Empty
            : string.Empty;
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

    private sealed record PortableChapterRule(string Name, string Pattern, bool IsEnabled);

    private static string DeduplicateName(
        string normalizedName,
        string currentId,
        IReadOnlyList<ChapterRule> existingRules)
    {
        if (IsAvailable(normalizedName))
        {
            return normalizedName;
        }

        for (var suffix = 2; suffix < 10_000; suffix++)
        {
            var candidate = $"{normalizedName}({suffix})";
            if (IsAvailable(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("规则名称冲突过多，请更换名称后重试。");

        bool IsAvailable(string candidate)
        {
            return existingRules.All(rule =>
                string.Equals(rule.Id, currentId, StringComparison.Ordinal) ||
                !string.Equals(rule.Name, candidate, StringComparison.Ordinal));
        }
    }
}
