namespace NovelSpeaker.Application.Playback.ActiveCache;

/// <summary>
/// Describes whether an active-cache batch was accepted.
/// </summary>
public enum ActiveCacheStartStatus
{
    Accepted,
    BatchAlreadyActive,
    BookNotFound,
    SelectedRuleUnavailable,
    NoChaptersSelected
}
