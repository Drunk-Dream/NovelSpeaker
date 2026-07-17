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
}
