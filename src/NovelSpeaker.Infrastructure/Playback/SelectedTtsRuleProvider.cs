using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Application.Speech;
using NovelSpeaker.Application.Speech.Compilation;

namespace NovelSpeaker.Infrastructure.Playback;

/// <summary>
/// Resolves the enabled rule selected in settings for runtime playback.
/// </summary>
public sealed class SelectedTtsRuleProvider : ISelectedTtsRuleProvider
{
    private readonly ITtsRuleRepository _repository;
    private readonly ITtsRuleLibraryService _libraryService;
    private readonly IAppSettingsService _settingsService;
    private readonly ITtsRuleNormalizer _ruleNormalizer;

    public SelectedTtsRuleProvider(
        ITtsRuleRepository repository,
        ITtsRuleLibraryService libraryService,
        IAppSettingsService settingsService,
        ITtsRuleNormalizer? ruleNormalizer = null)
    {
        _repository = repository;
        _libraryService = libraryService;
        _settingsService = settingsService;
        _ruleNormalizer = ruleNormalizer ?? new TtsRuleNormalizer();
    }

    public async Task<SelectedPlaybackRule?> GetSelectedRuleAsync(CancellationToken cancellationToken)
    {
        var settings = await _settingsService.LoadAsync(cancellationToken);
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
        await _libraryService.SelectRuleAsync(ruleId, cancellationToken);
        return await GetSelectedRuleAsync(cancellationToken);
    }
}
