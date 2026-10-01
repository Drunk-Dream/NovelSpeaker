using NovelSpeaker.Application.Books;
using NovelSpeaker.App.Shared.Dialogs;
using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.App.Shared.Presentation.Rules;
using NovelSpeaker.App.Shell.Navigation;
using NovelSpeaker.Domain.Books;

namespace NovelSpeaker.App.Features.Rules.Metadata;

public sealed class TextHeaderMetadataRulesViewModel : MetadataRuleWorkbenchViewModel
{
    private readonly ITextHeaderMetadataRuleRepository _rules;

    public TextHeaderMetadataRulesViewModel(ITextHeaderMetadataRuleRepository rules, IAppNavigator navigator,
        IAppDialogService dialogs, IAppFeedbackService feedback, IRuleDocumentInteraction documents)
        : base(navigator, dialogs, feedback, documents) => _rules = rules;

    public override string PageTitle => "正文头部元数据规则";
    protected override string DocumentFileName => "text-header-metadata-rules.json";
    protected override string DocumentRuleType => "textHeaderMetadata";
    protected override async Task<IReadOnlyList<MetadataRuleState>> ReadAsync(CancellationToken token) =>
        (await _rules.GetAllAsync(token)).Select(rule => new MetadataRuleState(
            rule.Id, rule.Name, rule.Pattern, rule.SortOrder, rule.IsEnabled, rule.CreatedAt, rule.UpdatedAt)).ToArray();
    protected override Task WriteAsync(MetadataRuleState rule, CancellationToken token) =>
        _rules.SaveAsync(new TextHeaderMetadataRule(rule.Id, rule.Name, rule.Pattern, rule.SortOrder,
            rule.IsEnabled, rule.CreatedAt, rule.UpdatedAt), token);
    protected override Task RemoveAsync(string id, CancellationToken token) => _rules.DeleteAsync(id, token);
    protected override Task WriteOrderAsync(IReadOnlyList<(string RuleId, int SortOrder)> order, CancellationToken token) =>
        _rules.SaveOrderAsync(order, token);
}
