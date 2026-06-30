using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using NovelSpeaker.App.Feedback;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Domain.Books;

namespace NovelSpeaker.App.ViewModels;

/// <summary>
/// Manages the global chapter-rule list used during import.
/// </summary>
public sealed partial class ChapterRulesViewModel : ObservableObject
{
    private readonly IChapterRuleRepository _repository;
    private readonly IAppFeedbackService _feedbackService;

    public ChapterRulesViewModel(IChapterRuleRepository repository, IAppFeedbackService feedbackService)
    {
        _repository = repository;
        _feedbackService = feedbackService;
    }

    public ObservableCollection<ChapterRuleDraft> Rules { get; } = [];

    [ObservableProperty]
    private string statusMessage = "在这里管理导入时使用的章节规则。";

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        var rules = await _repository.GetAllAsync(cancellationToken);
        Rules.ReplaceWith(rules, Map);
    }

    public async Task ImportDefaultsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var importedCount = await _repository.ImportDefaultsAsync(cancellationToken);
            await LoadAsync(cancellationToken);
            StatusMessage = importedCount > 0 ? "默认规则已导入。" : "默认规则已是最新版本。";
            _feedbackService.ShowSuccess(
                "默认规则导入完成",
                importedCount > 0
                    ? $"已新增或恢复 {importedCount} 条默认规则。"
                    : "当前默认规则已是最新版本。");
        }
        catch (Exception exception)
        {
            var projected = _feedbackService.Project(exception);
            StatusMessage = projected.UserMessage;
            _feedbackService.ShowProjectedNotification("默认规则导入失败", projected);
        }
    }

    public async Task SaveRuleAsync(ChapterRuleDraft rule, CancellationToken cancellationToken)
    {
        try
        {
            var utcNow = DateTime.UtcNow.ToString("O");
            await _repository.SaveAsync(new ChapterRule(
                rule.Id,
                rule.Name,
                rule.Pattern,
                rule.SortOrder,
                rule.IsEnabled,
                utcNow,
                utcNow), cancellationToken);
            await LoadAsync(cancellationToken);
            StatusMessage = $"已保存规则：{rule.Name}";
        }
        catch (Exception exception)
        {
            var projected = _feedbackService.Project(exception);
            StatusMessage = projected.UserMessage;
            _feedbackService.ShowProjectedNotification("章节规则保存失败", projected);
        }
    }

    public async Task DeleteRuleAsync(string ruleId, CancellationToken cancellationToken)
    {
        var ruleName = Rules.FirstOrDefault(rule => rule.Id == ruleId)?.Name ?? ruleId;
        var confirmation = await _feedbackService.ConfirmDeletionAsync(
            "删除章节规则",
            $"将删除章节规则“{ruleName}”。此操作不可撤销。",
            cancellationToken);

        if (confirmation != AppConfirmationDecision.Confirm)
        {
            StatusMessage = $"已取消删除规则：{ruleName}";
            return;
        }

        try
        {
            await _repository.DeleteAsync(ruleId, cancellationToken);
            await LoadAsync(cancellationToken);
            StatusMessage = "规则已删除。";
            _feedbackService.ShowSuccess("章节规则已删除", $"已删除规则：{ruleName}。");
        }
        catch (Exception exception)
        {
            var projected = _feedbackService.Project(exception);
            StatusMessage = projected.UserMessage;
            _feedbackService.ShowProjectedNotification("章节规则删除失败", projected);
        }
    }

    private static ChapterRuleDraft Map(ChapterRule rule) =>
        new(
            rule.Id,
            rule.Name,
            rule.Pattern,
            rule.SortOrder,
            rule.IsEnabled);
}
