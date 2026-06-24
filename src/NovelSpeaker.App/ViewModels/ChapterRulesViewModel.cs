using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Domain.Books;

namespace NovelSpeaker.App.ViewModels;

/// <summary>
/// Manages the global chapter-rule list used during import.
/// </summary>
public sealed partial class ChapterRulesViewModel : ObservableObject
{
    private readonly IChapterRuleRepository _repository;

    public ChapterRulesViewModel(IChapterRuleRepository repository)
    {
        _repository = repository;
    }

    public ObservableCollection<ChapterRuleDraft> Rules { get; } = [];

    [ObservableProperty]
    private string statusMessage = "在这里管理导入时使用的章节规则。";

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        var rules = await _repository.GetAllAsync(cancellationToken);
        Rules.Clear();
        foreach (var rule in rules)
        {
            Rules.Add(Map(rule));
        }
    }

    public async Task ImportDefaultsAsync(CancellationToken cancellationToken)
    {
        await _repository.ImportDefaultsAsync(cancellationToken);
        await LoadAsync(cancellationToken);
        StatusMessage = "默认规则已导入。";
    }

    public async Task SaveRuleAsync(ChapterRuleDraft rule, CancellationToken cancellationToken)
    {
        await _repository.SaveAsync(new ChapterRule(
            rule.Id,
            rule.Name,
            rule.Pattern,
            rule.SortOrder,
            rule.IsEnabled,
            DateTime.UtcNow.ToString("O"),
            DateTime.UtcNow.ToString("O")), cancellationToken);
        await LoadAsync(cancellationToken);
        StatusMessage = $"已保存规则：{rule.Name}";
    }

    public async Task DeleteRuleAsync(string ruleId, CancellationToken cancellationToken)
    {
        await _repository.DeleteAsync(ruleId, cancellationToken);
        await LoadAsync(cancellationToken);
        StatusMessage = "规则已删除。";
    }

    private static ChapterRuleDraft Map(ChapterRule rule) =>
        new(
            rule.Id,
            rule.Name,
            rule.Pattern,
            rule.SortOrder,
            rule.IsEnabled);
}
