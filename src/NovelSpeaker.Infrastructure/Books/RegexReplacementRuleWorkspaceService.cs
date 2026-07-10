using System.Text.RegularExpressions;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Domain.Books;

namespace NovelSpeaker.Infrastructure.Books;

/// <summary>Validates and persists regex rule editor fields without overwriting quick list changes.</summary>
public sealed class RegexReplacementRuleWorkspaceService : IRegexReplacementRuleWorkspaceService
{
    private const int SortOrderStep = 10;
    private readonly IRegexReplacementRuleRepository _repository;
    private readonly IRegexReplacementRuleErrorStore? _errorStore;

    public RegexReplacementRuleWorkspaceService(IRegexReplacementRuleRepository repository, IRegexReplacementRuleErrorStore? errorStore = null)
    {
        _repository = repository;
        _errorStore = errorStore;
    }

    public async Task<IReadOnlyList<RegexReplacementRuleListItem>> GetRulesAsync(CancellationToken cancellationToken)
    {
        var rules = await _repository.GetAllAsync(cancellationToken);
        return rules.OrderBy(rule => rule.SortOrder).ThenBy(rule => rule.Id)
            .Select(rule => new RegexReplacementRuleListItem(
                rule.Id,
                rule.Name,
                Summarize(rule.Pattern),
                rule.IsEnabled,
                rule.SortOrder,
                rule.Scope,
                _errorStore?.Current.GetValueOrDefault(rule.Id)))
            .ToArray();
    }

    public async Task<RegexReplacementRuleEditorModel?> GetEditorAsync(Guid ruleId, CancellationToken cancellationToken)
    {
        var rule = (await _repository.GetAllAsync(cancellationToken)).FirstOrDefault(item => item.Id == ruleId);
        return rule is null ? null : new RegexReplacementRuleEditorModel(rule.Id, rule.Name, rule.Pattern, rule.Replacement, rule.Scope);
    }

    public async Task<RegexReplacementRuleEditorModel> SaveEditorAsync(RegexReplacementRuleEditorModel editor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(editor);
        var name = (editor.Name ?? string.Empty).Trim();
        var pattern = (editor.Pattern ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("规则名称不能为空。");
        if (string.IsNullOrWhiteSpace(pattern)) throw new InvalidOperationException("正则表达式不能为空。");
        try { _ = new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)); }
        catch (ArgumentException exception) { throw new InvalidOperationException($"正则表达式无效：{exception.Message}"); }

        var rules = await _repository.GetAllAsync(cancellationToken);
        var existing = editor.Id is Guid id ? rules.FirstOrDefault(item => item.Id == id) : null;
        if (editor.Id is not null && existing is null) throw new InvalidOperationException("规则不存在，可能已被删除。");
        var now = DateTimeOffset.UtcNow;
        var saved = new RegexReplacementRule(
            existing?.Id ?? Guid.NewGuid(), name, existing?.IsEnabled ?? true,
            existing?.SortOrder ?? (rules.Count == 0 ? SortOrderStep : rules.Max(item => item.SortOrder) + SortOrderStep),
            pattern, editor.Replacement ?? string.Empty, editor.Scope, existing?.CreatedAt ?? now, now);
        await _repository.SaveAsync(saved, cancellationToken);
        return new RegexReplacementRuleEditorModel(saved.Id, saved.Name, saved.Pattern, saved.Replacement, saved.Scope);
    }

    public Task SetRuleEnabledAsync(Guid ruleId, bool isEnabled, CancellationToken cancellationToken) => _repository.UpdateEnabledAsync(ruleId, isEnabled, cancellationToken);

    public async Task SaveOrderAsync(IReadOnlyList<Guid> orderedRuleIds, CancellationToken cancellationToken)
    {
        var rules = await _repository.GetAllAsync(cancellationToken);
        if (orderedRuleIds.Count != rules.Count || orderedRuleIds.Distinct().Count() != rules.Count || rules.Any(rule => !orderedRuleIds.Contains(rule.Id)))
            throw new InvalidOperationException("排序保存失败，请刷新后重试。");
        await _repository.SaveOrderAsync(orderedRuleIds.Select((id, index) => (id, (index + 1) * SortOrderStep)).ToArray(), cancellationToken);
    }

    public Task DeleteRuleAsync(Guid ruleId, CancellationToken cancellationToken) => _repository.DeleteAsync(ruleId, cancellationToken);

    private static string Summarize(string pattern)
    {
        var summary = Regex.Replace(pattern, @"\s+", " ");
        return summary.Length <= 80 ? summary : $"{summary[..77]}...";
    }
}
