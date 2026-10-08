using NovelSpeaker.Domain.Books;

namespace NovelSpeaker.Application.Books;

public sealed record LocalSourceImportTarget(Book Book, BookSource? Source, LocalBookSource? LocalSource);
