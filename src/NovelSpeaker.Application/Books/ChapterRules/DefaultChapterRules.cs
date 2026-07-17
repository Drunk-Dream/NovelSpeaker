namespace NovelSpeaker.Application.Books.ChapterRules;

/// <summary>Describes one stable built-in chapter rule used by persistence compatibility.</summary>
public sealed record DefaultChapterRuleDefinition(
    string Id,
    string Name,
    string Pattern,
    int SortOrder);

/// <summary>Provides the stable built-in chapter-rule identities and definitions.</summary>
public static class DefaultChapterRules
{
    public static IReadOnlyList<DefaultChapterRuleDefinition> All { get; } =
    [
        new("builtin:chapter-number", "章节数字", @"^\s*第[0-9一二三四五六七八九十百千零两]+章(?:\s+.+)?\s*$", 10),
        new("builtin:chapter-volume", "章节卷标", @"^\s*第[0-9一二三四五六七八九十百千零两]+卷(?:\s+.+)?\s*$", 20),
        new("builtin:chapter-preface", "序章楔子", @"^\s*(序章|楔子|前言)\s*$", 30),
        new("builtin:chapter-epilogue", "尾声后记", @"^\s*(尾声|后记|番外(?:\s*.+)?)\s*$", 40)
    ];

    public static bool IsBuiltInId(string ruleId)
    {
        return All.Any(definition => string.Equals(definition.Id, ruleId, StringComparison.Ordinal));
    }
}
