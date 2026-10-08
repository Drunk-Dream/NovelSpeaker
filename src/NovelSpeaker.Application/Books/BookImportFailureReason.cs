namespace NovelSpeaker.Application.Books;

public enum BookImportFailureReason
{
    UnsupportedEncoding,
    NoValidChapters,
    FileReadFailed,
    TextNormalizationFailed,
    ChapterRuleTimedOut
}
