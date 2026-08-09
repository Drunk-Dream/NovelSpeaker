using NovelSpeaker.Domain.Speech;

namespace NovelSpeaker.Application.Speech.Rules;

internal sealed class TtsRuleEditorUseCase(
    ITtsRuleRepository repository,
    TimeProvider timeProvider) : ITtsRuleEditorUseCase
{
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
        var saved = await repository.GetByIdAsync(id, cancellationToken) ?? rule with { Id = id };
        return saved;
    }

    public async Task SetRuleEnabledAsync(
        long ruleId,
        bool isEnabled,
        CancellationToken cancellationToken)
    {
        var rule = await repository.GetByIdAsync(ruleId, cancellationToken)
            ?? throw new InvalidOperationException("未找到要更新的规则。");
        await repository.SaveAsync(
            rule with { IsEnabled = isEnabled, UpdatedAt = timeProvider.GetUtcNow() },
            cancellationToken);
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
