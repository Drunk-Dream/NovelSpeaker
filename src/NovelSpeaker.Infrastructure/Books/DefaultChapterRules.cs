namespace NovelSpeaker.Infrastructure.Books;

internal static class DefaultChapterRules
{
    public static IReadOnlyList<(string Name, string Pattern)> All { get; } =
    [
        ("章节数字", @"^\s*第[0-9一二三四五六七八九十百千零两]+章(?:\s+.+)?\s*$"),
        ("章节卷标", @"^\s*第[0-9一二三四五六七八九十百千零两]+卷(?:\s+.+)?\s*$"),
        ("序章楔子", @"^\s*(序章|楔子|前言)\s*$"),
        ("尾声后记", @"^\s*(尾声|后记|番外(?:\s*.+)?)\s*$")
    ];
}
