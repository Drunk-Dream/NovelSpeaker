namespace NovelSpeaker.App.Shared.Presentation.Rules;

public sealed record RuleReorderRequest(
    object Source,
    object Target,
    RuleDropPlacement Placement);
