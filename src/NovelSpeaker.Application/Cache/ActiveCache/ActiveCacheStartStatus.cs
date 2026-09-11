namespace NovelSpeaker.Application.Cache.ActiveCache;

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
