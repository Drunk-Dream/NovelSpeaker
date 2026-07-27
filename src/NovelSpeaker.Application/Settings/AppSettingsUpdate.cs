namespace NovelSpeaker.Application.Settings;

using NovelSpeaker.Domain.Settings;

/// <summary>
/// Describes a partial settings update where unspecified fields keep their current value.
/// </summary>
public sealed record AppSettingsUpdate
{
    public bool? EnableLongParagraphSplitting { get; init; }

    public int? LongParagraphThreshold { get; init; }

    public int? DefaultSpeakSpeed { get; init; }

    public int? PrefetchCount { get; init; }

    public string? LogLevel { get; init; }

    public string? Theme { get; init; }

    public string? BookFileNameTemplate { get; init; }

    public long? CacheLimitBytes { get; init; }

    public long? SelectedTtsRuleId { get; init; }

    public bool ClearSelectedTtsRuleId { get; init; }

    public MainWindowCloseBehavior? MainWindowCloseBehavior { get; init; }

    public bool? StartMinimizedToTray { get; init; }
}
