namespace NovelSpeaker.Application.Speech.Rules;

/// <summary>
/// Resolves the enabled rule selected in settings for runtime playback.
/// </summary>
public sealed class SelectedTtsRuleProvider : ISelectedTtsRuleProvider
{
    public Task<SelectedPlaybackRule?> GetSelectedRuleAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<SelectedPlaybackRule?>(null);
    }

    public Task<SelectedPlaybackRule?> SelectRuleAsync(long ruleId, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Legacy TTS rule selection is unavailable while speech providers are being migrated.");
}
