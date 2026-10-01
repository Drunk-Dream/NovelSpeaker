namespace NovelSpeaker.App.Features.Rules.Metadata;

public sealed record MetadataRuleState(
    string Id, string Name, string Pattern, int SortOrder, bool IsEnabled,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
