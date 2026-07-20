using NovelSpeaker.Application.Settings;
using NovelSpeaker.Application.Speech;
using NovelSpeaker.Application.Speech.Compilation;
using NovelSpeaker.Application.Speech.Rules;

namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Resolves the enabled rule selected in settings for runtime playback.
/// </summary>
public sealed class SelectedTtsRuleProvider : ISelectedTtsRuleProvider
{
    private readonly ITtsRuleRepository _repository;
    private readonly ITtsRuleSelectionUseCase _selection;
    private readonly IAppSettingsService _settingsService;
    private readonly ITtsRuleNormalizer _ruleNormalizer;

    public SelectedTtsRuleProvider(
        ITtsRuleRepository repository,
        ITtsRuleSelectionUseCase selection,
        IAppSettingsService settingsService,
        ITtsRuleNormalizer? ruleNormalizer = null)
    {
        _repository = repository;
        _selection = selection;
        _settingsService = settingsService;
        _ruleNormalizer = ruleNormalizer ?? new TtsRuleNormalizer();
    }

    public async Task<SelectedPlaybackRule?> GetSelectedRuleAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var settings = _settingsService.Current;
        if (settings.SelectedTtsRuleId is null)
        {
            return null;
        }

        var rule = await _repository.GetByIdAsync(settings.SelectedTtsRuleId.Value, cancellationToken);
        if (rule is null || !rule.IsEnabled)
        {
            return null;
        }

        return new SelectedPlaybackRule(
            rule.Id,
            rule.Name,
            rule,
            _ruleNormalizer.Normalize(rule));
    }

    public async Task<SelectedPlaybackRule?> SelectRuleAsync(long ruleId, CancellationToken cancellationToken)
    {
        await _selection.SelectRuleAsync(ruleId, cancellationToken);
        return await GetSelectedRuleAsync(cancellationToken);
    }
}
