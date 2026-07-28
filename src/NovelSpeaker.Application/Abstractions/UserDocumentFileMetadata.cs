namespace NovelSpeaker.Application.Abstractions;

public sealed record UserDocumentFileMetadata(
    string FilePath,
    string FileName,
    string Extension,
    long Length);
