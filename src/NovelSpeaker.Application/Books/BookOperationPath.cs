namespace NovelSpeaker.Application.Books;

public sealed record BookOperationPath(
    string OriginalStorageKey,
    string StagedStorageKey,
    bool IsDirectory);
