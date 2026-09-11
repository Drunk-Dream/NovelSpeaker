using NovelSpeaker.Application.Speech.Compilation;
using NovelSpeaker.Domain.Speech;

namespace NovelSpeaker.Application.Speech.Rules;

internal sealed class TtsRuleEditorUseCase(
    ITtsRuleRepository repository,
    TimeProvider timeProvider,
    ITtsRuleNormalizer? ruleNormalizer = null) : ITtsRuleEditorUseCase
{
    private readonly ITtsRuleNormalizer _ruleNormalizer = ruleNormalizer ?? new TtsRuleNormalizer();

    public event EventHandler<TtsRuleChangedEventArgs>? Changed;

    public async Task<TtsRuleEditorModel?> GetEditorAsync(long ruleId, CancellationToken cancellationToken)
    {
        var rule = await repository.GetByIdAsync(ruleId, cancellationToken);
        return rule is null ? null : TtsRuleModelMapper.ToEditor(rule);
    }

    public async Task<TtsRuleValidationResult> ValidateEditorAsync(TtsRuleEditorModel editor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(editor);
        cancellationToken.ThrowIfCancellationRequested();
        var normalized = TtsRuleModelMapper.Normalize(editor);
        var errors = TtsRuleEditorValidator.Validate(normalized);
        return new TtsRuleValidationResult(errors.Count == 0, errors, normalized);
    }

    public async Task<HttpTtsRule> SaveEditorAsync(TtsRuleEditorModel editor, CancellationToken cancellationToken)
    {
        var validation = await ValidateEditorAsync(editor, cancellationToken);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException(string.Join(" ", validation.Errors));
        }

        var existing = validation.NormalizedModel.Id is > 0
            ? await repository.GetByIdAsync(validation.NormalizedModel.Id.Value, cancellationToken)
            : null;
        var model = existing is null
            ? validation.NormalizedModel
            : validation.NormalizedModel with { IsEnabled = existing.IsEnabled };
        var rule = TtsRuleModelMapper.BuildRule(model, existing, timeProvider.GetUtcNow());
        rule = TtsRuleModelMapper.EnsureUniqueName(rule, await repository.GetAllAsync(cancellationToken), existing?.Id);
        var id = await repository.SaveAsync(rule, cancellationToken);
        var persisted = rule with { Id = id };
        if (existing is null || !TtsRuleFingerprint.Create(_ruleNormalizer.Normalize(existing))
                .Equals(TtsRuleFingerprint.Create(_ruleNormalizer.Normalize(persisted))))
        {
            Changed?.Invoke(this, new TtsRuleChangedEventArgs(persisted.Id));
        }

        return await repository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false) ?? persisted;
    }

    public async Task SetRuleEnabledAsync(
        long ruleId,
        bool isEnabled,
        CancellationToken cancellationToken)
    {
        var rule = await repository.GetByIdAsync(ruleId, cancellationToken)
            ?? throw new InvalidOperationException("未找到要更新的规则。");
        var updated = rule with { IsEnabled = isEnabled, UpdatedAt = timeProvider.GetUtcNow() };
        await repository.SaveAsync(updated, cancellationToken);
        if (rule.IsEnabled != isEnabled)
        {
            Changed?.Invoke(this, new TtsRuleChangedEventArgs(ruleId));
        }
    }

    public async Task<TtsRuleDraftPreparationResult> PrepareDraftAsync(
        TtsRuleEditorModel editor,
        CancellationToken cancellationToken)
    {
        var validation = await ValidateEditorAsync(editor, cancellationToken);
        if (!validation.IsValid)
        {
            return new TtsRuleDraftPreparationResult(validation, null);
        }

        var existing = validation.NormalizedModel.Id is > 0
            ? await repository.GetByIdAsync(validation.NormalizedModel.Id.Value, cancellationToken)
            : null;
        var candidate = TtsRuleModelMapper.BuildRule(
            validation.NormalizedModel,
            existing,
            timeProvider.GetUtcNow());
        return new TtsRuleDraftPreparationResult(validation, candidate);
    }

    public async Task<string> ExportEditorJsonAsync(TtsRuleEditorModel editor, CancellationToken cancellationToken)
    {
        var validation = await ValidateEditorAsync(editor, cancellationToken);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException(string.Join(" ", validation.Errors));
        }
        var existing = validation.NormalizedModel.Id is > 0
            ? await repository.GetByIdAsync(validation.NormalizedModel.Id.Value, cancellationToken)
            : null;
        return TtsRuleJsonSerializer.Serialize(TtsRuleModelMapper.BuildRule(validation.NormalizedModel, existing, timeProvider.GetUtcNow()));
    }

}
