using NovelSpeaker.Domain.Books;

namespace NovelSpeaker.Application.Books.ChapterRules;

/// <summary>Provides previews and application of built-in chapter-rule defaults.</summary>
public sealed class ChapterRuleManagementService : IChapterRuleManagementService
{
    private readonly IChapterRuleRepository _repository;
    private readonly TimeProvider _timeProvider;

    public ChapterRuleManagementService(IChapterRuleRepository repository, TimeProvider timeProvider)
    {
        _repository = repository;
        _timeProvider = timeProvider;
    }

    public async Task<ChapterRuleDefaultsPreview> PreviewDefaultsAsync(
        ChapterRuleDefaultsMode mode,
        CancellationToken cancellationToken)
    {
        var existingRules = await _repository.GetAllAsync(cancellationToken);
        return new ChapterRuleDefaultsPreview(mode, BuildPreview(mode, existingRules));
    }

    public async Task<ChapterRuleDefaultsApplyResult> ApplyDefaultsAsync(
        ChapterRuleDefaultsMode mode,
        CancellationToken cancellationToken)
    {
        var existingRules = await _repository.GetAllAsync(cancellationToken);
        var changes = BuildPreview(mode, existingRules);

        switch (mode)
        {
            case ChapterRuleDefaultsMode.ImportDefaults:
                await _repository.ImportDefaultsAsync(cancellationToken);
                break;
            case ChapterRuleDefaultsMode.RestoreDefaults:
                await RestoreDefaultsAsync(existingRules, cancellationToken);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
        }

        return new ChapterRuleDefaultsApplyResult(
            mode,
            changes.Count(change => change.ChangeKind == ChapterRuleChangeKind.Added),
            changes.Count(change => change.ChangeKind == ChapterRuleChangeKind.Updated),
            changes.Count(change => change.ChangeKind == ChapterRuleChangeKind.Unchanged));
    }

    private static IReadOnlyList<ChapterRuleChangeSummary> BuildPreview(
        ChapterRuleDefaultsMode mode,
        IReadOnlyList<ChapterRule> existingRules)
    {
        return DefaultChapterRules.All.Select(definition =>
        {
            var existing = existingRules.FirstOrDefault(rule =>
                string.Equals(rule.Id, definition.Id, StringComparison.Ordinal));
            if (existing is null)
            {
                return new ChapterRuleChangeSummary(
                    definition.Id,
                    definition.Name,
                    definition.Pattern,
                    definition.SortOrder,
                    ChapterRuleChangeKind.Added);
            }

            var needsUpdate = mode switch
            {
                ChapterRuleDefaultsMode.ImportDefaults =>
                    !string.Equals(existing.Name, definition.Name, StringComparison.Ordinal) ||
                    !string.Equals(existing.Pattern, definition.Pattern, StringComparison.Ordinal),
                ChapterRuleDefaultsMode.RestoreDefaults =>
                    !string.Equals(existing.Name, definition.Name, StringComparison.Ordinal) ||
                    !string.Equals(existing.Pattern, definition.Pattern, StringComparison.Ordinal) ||
                    existing.SortOrder != definition.SortOrder ||
                    !existing.IsEnabled,
                _ => false
            };

            return new ChapterRuleChangeSummary(
                definition.Id,
                definition.Name,
                definition.Pattern,
                definition.SortOrder,
                needsUpdate ? ChapterRuleChangeKind.Updated : ChapterRuleChangeKind.Unchanged);
        }).ToArray();
    }

    private async Task RestoreDefaultsAsync(
        IReadOnlyList<ChapterRule> existingRules,
        CancellationToken cancellationToken)
    {
        foreach (var definition in DefaultChapterRules.All)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var existing = existingRules.FirstOrDefault(rule =>
                string.Equals(rule.Id, definition.Id, StringComparison.Ordinal));
            var utcNow = _timeProvider.GetUtcNow();
            await _repository.SaveAsync(new ChapterRule(
                definition.Id,
                definition.Name,
                definition.Pattern,
                definition.SortOrder,
                true,
                existing?.CreatedAt ?? utcNow,
                utcNow), cancellationToken);
        }
    }
}
