using NovelSpeaker.Domain.Settings;

namespace NovelSpeaker.App.Features.GeneralSettings;

public sealed record CloseBehaviorOption(MainWindowCloseBehavior Value, string DisplayName);
