using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Application.Books;

namespace NovelSpeaker.Infrastructure.Persistence.Books;

/// <summary>
/// Persists validated user-editable book metadata.
/// </summary>
public sealed class BookMetadataUpdateService : IBookMetadataUpdateService
{
    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly TimeProvider _timeProvider;

    public BookMetadataUpdateService(ISqliteConnectionFactory connectionFactory, TimeProvider? timeProvider = null)
    {
        _connectionFactory = connectionFactory;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<BookDetailsHeader> UpdateMetadataAsync(BookMetadataUpdateRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var title = request.Title.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new InvalidOperationException("书名不能为空。");
        }

        var author = string.IsNullOrWhiteSpace(request.Author) ? null : request.Author.Trim();
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var command = connection.CreateCommand();
            command.Transaction = (Microsoft.Data.Sqlite.SqliteTransaction)transaction;
            command.CommandText =
                """
                UPDATE Books
                SET Title = $title, Author = $author, UpdatedAt = $updatedAt
                WHERE Id = $bookId;
                """;
            command.Parameters.AddWithValue("$bookId", request.BookId);
            command.Parameters.AddWithValue("$title", title);
            command.Parameters.AddWithValue("$author", (object?)author ?? DBNull.Value);
            command.Parameters.AddWithValue("$updatedAt", SqliteDateTimeMapper.Format(_timeProvider.GetUtcNow()));
            if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 0)
            {
                throw new InvalidOperationException("未找到要更新的书籍。");
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return new BookDetailsHeader(request.BookId, title, author);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }
}
