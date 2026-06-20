namespace NovelSpeaker.Application.Books;

public enum BookImportFailureReason
{
    UnsupportedEncoding,
    DuplicateBook,
    NoValidChapters,
    FileReadFailed,
    TextNormalizationFailed
}
