namespace NovelSpeaker.Application.Playback;

/// <summary>
/// Resolves and updates the enabled HTTP TTS rule used by playback sessions.
/// </summary>
public interface ISelectedTtsRuleProvider
{
    Task<SelectedPlaybackRule?> GetSelectedRuleAsync(CancellationToken cancellationToken);

    Task<SelectedPlaybackRule?> SelectRuleAsync(long ruleId, CancellationToken cancellationToken);
}
