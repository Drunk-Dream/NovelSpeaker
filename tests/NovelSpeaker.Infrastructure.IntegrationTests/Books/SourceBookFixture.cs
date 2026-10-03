using NovelSpeaker.Application.Books;
using NovelSpeaker.Domain.Books;
using NovelSpeaker.Infrastructure.Persistence;
using NovelSpeaker.Infrastructure.Persistence.Books;

namespace NovelSpeaker.Infrastructure.IntegrationTests.Books;

internal static class SourceBookFixture
{
    public static async Task SaveAsync(SqliteConnectionFactory factory, string bookId, IReadOnlyList<string> titles,
        string? contentPath = null, IReadOnlyList<int>? sortOrders = null,
        string? title = null, string? author = null, string? description = null, IReadOnlyList<string>? chapterIds = null)
    {
        var repository = new BookImportRepository(factory);
        var target = await repository.GetTargetAsync(bookId, CancellationToken.None);
        var now = DateTimeOffset.UtcNow;
        var sourceId = target?.Source?.Id ?? $"local:{bookId}";
        var book = target?.Book ?? new Book(bookId, title ?? $"书籍 {bookId}", author, sourceId, now, null, now, description);
        if (titles.Count == 0)
        {
            await using var connection = await factory.OpenConnectionAsync(CancellationToken.None);
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO Books (Id, Title, ImportedAt, UpdatedAt) VALUES ($id, $title, $time, $time);";
            command.Parameters.AddWithValue("$id", bookId);
            command.Parameters.AddWithValue("$title", book.Title);
            command.Parameters.AddWithValue("$time", now.ToString("O"));
            await command.ExecuteNonQueryAsync();
            return;
        }
        var source = new BookSource(sourceId, bookId, SourceType.Local, book.Title, book.Author, book.Description, now, now);
        var local = new LocalBookSource(sourceId, $"{bookId}.txt", contentPath ?? $"Books/{bookId}/{Guid.NewGuid():N}.txt",
            "fixture-hash", "utf-8", now, now);
        var chapters = titles.Select((chapterTitle, index) => new Chapter(chapterIds?[index] ?? $"{bookId}:{Guid.NewGuid():N}", sourceId,
            index, sortOrders?[index] ?? index, chapterTitle)).ToArray();
        var contents = chapters.Select((chapter, index) => new LocalChapterContent(chapter.Id, index * 4, 3)).ToArray();
        var operationId = Guid.NewGuid().ToString("N");
        var journal = new SqliteBookOperationJournal(factory, TimeProvider.System);
        await journal.CreateAsync(new BookOperationRecord(operationId,
            BookOperationKind.Import, BookOperationPhase.Staged, bookId, [], now), CancellationToken.None);
        await repository.SaveAsync(new LocalSourceImportSnapshot(book, source, local, chapters, contents,
            target is null, target?.LocalSource?.StoredContentPath), operationId, CancellationToken.None);
        await journal.SetPhaseAsync(operationId, BookOperationPhase.Completed, CancellationToken.None);
    }
}
