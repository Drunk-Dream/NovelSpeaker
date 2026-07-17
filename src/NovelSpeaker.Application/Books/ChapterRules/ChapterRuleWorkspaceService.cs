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
        return new ChapterRuleListItem(
            rule.Id,
            rule.Name,
            RulePatternValidation.Summarize(rule.Pattern),
            rule.IsEnabled,
            rule.SortOrder,
            DefaultChapterRules.IsBuiltInId(rule.Id));
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
