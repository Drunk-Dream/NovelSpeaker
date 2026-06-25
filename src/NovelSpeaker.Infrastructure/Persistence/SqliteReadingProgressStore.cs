using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Application.Playback;

namespace NovelSpeaker.Infrastructure.Persistence;

/// <summary>
/// Persists and restores reading progress snapshots from SQLite.
/// </summary>
public sealed class SqliteReadingProgressStore : IReadingProgressStore
{
    private readonly ISqliteConnectionFactory _connectionFactory;

    public SqliteReadingProgressStore(ISqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task SaveAsync(PlaybackProgressUpdate progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (Microsoft.Data.Sqlite.SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var updatedAt = DateTime.UtcNow.ToString("O");

        var upsertCommand = connection.CreateCommand();
        upsertCommand.Transaction = transaction;
        upsertCommand.CommandText =
            """
            INSERT INTO ReadingProgress (BookId, ChapterIndex, SegmentIndex, CharacterOffset, AudioPositionMilliseconds, UpdatedAt)
            VALUES ($bookId, $chapterIndex, $segmentIndex, $characterOffset, $audioPositionMilliseconds, $updatedAt)
            ON CONFLICT(BookId) DO UPDATE SET
                ChapterIndex = excluded.ChapterIndex,
                SegmentIndex = excluded.SegmentIndex,
                CharacterOffset = excluded.CharacterOffset,
                AudioPositionMilliseconds = excluded.AudioPositionMilliseconds,
                UpdatedAt = excluded.UpdatedAt;
            """;
        upsertCommand.Parameters.AddWithValue("$bookId", progress.BookId);
        upsertCommand.Parameters.AddWithValue("$chapterIndex", progress.ChapterIndex);
        upsertCommand.Parameters.AddWithValue("$segmentIndex", progress.SegmentIndex);
        upsertCommand.Parameters.AddWithValue("$characterOffset", progress.CharacterOffset);
        upsertCommand.Parameters.AddWithValue("$audioPositionMilliseconds", progress.AudioPositionMilliseconds);
        upsertCommand.Parameters.AddWithValue("$updatedAt", updatedAt);
        await upsertCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        var bookCommand = connection.CreateCommand();
        bookCommand.Transaction = transaction;
        bookCommand.CommandText =
            """
            UPDATE Books
            SET LastPlayedAt = $lastPlayedAt
            WHERE Id = $bookId;
            """;
        bookCommand.Parameters.AddWithValue("$bookId", progress.BookId);
        bookCommand.Parameters.AddWithValue("$lastPlayedAt", updatedAt);
        await bookCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<ReadingProgressEntry?> GetAsync(string bookId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bookId);

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT BookId, ChapterIndex, SegmentIndex, CharacterOffset, AudioPositionMilliseconds, UpdatedAt
            FROM ReadingProgress
            WHERE BookId = $bookId
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$bookId", bookId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await ReadSingleAsync(reader, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ReadingProgressEntry?> GetMostRecentAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT rp.BookId, rp.ChapterIndex, rp.SegmentIndex, rp.CharacterOffset, rp.AudioPositionMilliseconds, rp.UpdatedAt
            FROM ReadingProgress rp
            INNER JOIN Books b ON b.Id = rp.BookId
            ORDER BY rp.UpdatedAt DESC, b.LastPlayedAt DESC
            LIMIT 1;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await ReadSingleAsync(reader, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<ReadingProgressEntry?> ReadSingleAsync(Microsoft.Data.Sqlite.SqliteDataReader reader, CancellationToken cancellationToken)
    {
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new ReadingProgressEntry(
            reader.GetString(0),
            reader.GetInt32(1),
            reader.GetInt32(2),
            reader.GetInt32(3),
            reader.GetInt64(4),
            reader.GetString(5));
    }
}
