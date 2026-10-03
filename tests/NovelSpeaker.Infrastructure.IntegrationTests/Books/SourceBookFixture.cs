using NovelSpeaker.Application.Books;
using NovelSpeaker.Domain.Books;
using NovelSpeaker.Infrastructure.Persistence;
using NovelSpeaker.Infrastructure.Persistence.Books;

namespace NovelSpeaker.Infrastructure.IntegrationTests.Books;

internal static class SourceBookFixture
{
    public static async Task SaveAsync(SqliteConnectionFactory factory, string bookId, IReadOnlyList<string> titles,
        string? contentPath = null, IReadOnlyList<int>? sortOrders = null)
    {
        var repository = new BookImportRepository(factory);
        var target = await repository.GetTargetAsync(bookId, CancellationToken.None);
        var now = DateTimeOffset.UtcNow;
        var sourceId = target?.Source?.Id ?? $"local:{bookId}";
        var book = target?.Book ?? new Book(bookId, $"书籍 {bookId}", null, sourceId, now, null, now);
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
        var chapters = titles.Select((title, index) => new Chapter($"{bookId}:{Guid.NewGuid():N}", sourceId,
            index, sortOrders?[index] ?? index, title)).ToArray();
        var contents = chapters.Select((chapter, index) => new LocalChapterContent(chapter.Id, index * 4, 3)).ToArray();
        var operationId = Guid.NewGuid().ToString("N");
        await new SqliteBookOperationJournal(factory, TimeProvider.System).CreateAsync(new BookOperationRecord(operationId,
            BookOperationKind.Import, BookOperationPhase.Staged, bookId, [], now), CancellationToken.None);
        await repository.SaveAsync(new LocalSourceImportSnapshot(book, source, local, chapters, contents,
            target is null, target?.LocalSource?.StoredContentPath), operationId, CancellationToken.None);
    }
}
