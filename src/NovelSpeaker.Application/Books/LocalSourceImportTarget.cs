using NovelSpeaker.Domain.Books;

namespace NovelSpeaker.Application.Books;

public sealed record LocalSourceImportTarget(Book Book, BookSourceBinding? Binding, LocalBookSourceBinding? LocalBinding);
