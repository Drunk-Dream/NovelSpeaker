namespace NovelSpeaker.App.Shared.Feedback;

public enum UiMessageSeverity
{
    Information,
    Warning,
    Error
}

public sealed record ProjectedUiError(
    string UserMessage,
    UiMessageSeverity Severity,
    bool IsSilent);
