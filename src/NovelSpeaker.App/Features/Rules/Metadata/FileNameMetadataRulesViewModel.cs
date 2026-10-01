using NovelSpeaker.Application.Books;
using NovelSpeaker.App.Shared.Dialogs;
using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.App.Shared.Presentation.Rules;
using NovelSpeaker.App.Shell.Navigation;
using NovelSpeaker.Domain.Books;

namespace NovelSpeaker.App.Features.Rules.Metadata;

public sealed class FileNameMetadataRulesViewModel : MetadataRuleWorkbenchViewModel
{
    private readonly IFileNameMetadataRuleRepository _rules;

    public FileNameMetadataRulesViewModel(IFileNameMetadataRuleRepository rules, IAppNavigator navigator,
        IAppDialogService dialogs, IAppFeedbackService feedback, IRuleDocumentInteraction documents)
        : base(navigator, dialogs, feedback, documents) => _rules = rules;

    public override string PageTitle => "文件名元数据规则";
    protected override string DocumentFileName => "filename-metadata-rules.json";
    protected override string DocumentRuleType => "fileNameMetadata";
    protected override async Task<IReadOnlyList<MetadataRuleState>> ReadAsync(CancellationToken token) =>
        (await _rules.GetAllAsync(token)).Select(rule => new MetadataRuleState(
            rule.Id, rule.Name, rule.Pattern, rule.SortOrder, rule.IsEnabled, rule.CreatedAt, rule.UpdatedAt)).ToArray();
    protected override Task WriteAsync(MetadataRuleState rule, CancellationToken token) =>
        _rules.SaveAsync(new FileNameMetadataRule(rule.Id, rule.Name, rule.Pattern, rule.SortOrder,
            rule.IsEnabled, rule.CreatedAt, rule.UpdatedAt), token);
    protected override Task RemoveAsync(string id, CancellationToken token) => _rules.DeleteAsync(id, token);
    protected override Task WriteOrderAsync(IReadOnlyList<(string RuleId, int SortOrder)> order, CancellationToken token) =>
        _rules.SaveOrderAsync(order, token);
}
