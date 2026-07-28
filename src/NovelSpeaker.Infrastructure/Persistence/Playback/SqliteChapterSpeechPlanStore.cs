using Microsoft.Data.Sqlite;
using NovelSpeaker.Application.Cache;
using NovelSpeaker.Application.Playback.Cache;
using NovelSpeaker.Domain.Books;

namespace NovelSpeaker.Infrastructure.Persistence.Playback;

/// <summary>
/// Stores one current plan per chapter and replaces its segments in one short transaction.
/// </summary>
public sealed class SqliteChapterSpeechPlanStore : IChapterSpeechPlanStore
{
    private readonly ISqliteConnectionFactory _connectionFactory;

    public SqliteChapterSpeechPlanStore(ISqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<ChapterSpeechPlan?> GetAsync(
        string chapterId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chapterId);
        await using var connection = await _connectionFactory
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);

        var planCommand = connection.CreateCommand();
        planCommand.CommandText =
            """
            SELECT ChapterRevisionHash, TextProfileFingerprint, PlanOutputHash,
                   State, BodySegmentCount, UpdatedAt
            FROM ChapterSpeechPlans
            WHERE ChapterId = $chapterId
            LIMIT 1;
            """;
        planCommand.Parameters.AddWithValue("$chapterId", chapterId);
        byte[] chapterRevisionHash;
        byte[] textProfileFingerprint;
        byte[] planOutputHash;
        ChapterSpeechPlanState state;
        int bodySegmentCount;
        DateTimeOffset updatedAt;
        await using (var planReader = await planCommand
                         .ExecuteReaderAsync(cancellationToken)
                         .ConfigureAwait(false))
        {
            if (!await planReader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            chapterRevisionHash = planReader.GetFieldValue<byte[]>(0);
            textProfileFingerprint = planReader.GetFieldValue<byte[]>(1);
            planOutputHash = planReader.GetFieldValue<byte[]>(2);
            state = (ChapterSpeechPlanState)planReader.GetInt32(3);
            bodySegmentCount = planReader.GetInt32(4);
            updatedAt = SqliteDateTimeMapper.Parse(planReader.GetString(5));
        }

        var plan = new ChapterSpeechPlan(
            chapterId,
            new Fingerprint(chapterRevisionHash),
            new TextProfileFingerprint(
                TextProfileFingerprint.CurrentSchemaVersion,
                new Fingerprint(textProfileFingerprint)),
            new Fingerprint(planOutputHash),
            state,
            bodySegmentCount,
            updatedAt,
            await ReadSegmentsAsync(connection, chapterId, cancellationToken).ConfigureAwait(false));
        return plan;
    }

    public async Task SaveAsync(
        ChapterSpeechPlan plan,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        await using var connection = await _connectionFactory
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            var existingCommand = connection.CreateCommand();
            existingCommand.Transaction = transaction;
            existingCommand.CommandText =
                "SELECT PlanOutputHash FROM ChapterSpeechPlans WHERE ChapterId = $chapterId LIMIT 1;";
            existingCommand.Parameters.AddWithValue("$chapterId", plan.ChapterId);
            var existingHash = await existingCommand
                .ExecuteScalarAsync(cancellationToken)
                .ConfigureAwait(false);
            var outputIsUnchanged = existingHash is byte[] hash &&
                hash.AsSpan().SequenceEqual(plan.PlanOutputHash.Bytes.Span);

            if (outputIsUnchanged)
            {
                await UpdateHeaderAsync(connection, transaction, plan, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                await ReplacePlanAsync(connection, transaction, plan, cancellationToken)
                    .ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private static async Task UpdateHeaderAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ChapterSpeechPlan plan,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            UPDATE ChapterSpeechPlans
            SET ChapterRevisionHash = $chapterRevisionHash,
                TextProfileFingerprint = $textProfileFingerprint,
                State = $state,
                BodySegmentCount = $bodySegmentCount,
                UpdatedAt = $updatedAt
            WHERE ChapterId = $chapterId;
            """;
        AddPlanHeaderParameters(command, plan);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ReplacePlanAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ChapterSpeechPlan plan,
        CancellationToken cancellationToken)
    {
        var header = connection.CreateCommand();
        header.Transaction = transaction;
        header.CommandText =
            """
            INSERT INTO ChapterSpeechPlans
                (ChapterId, ChapterRevisionHash, TextProfileFingerprint, PlanOutputHash,
                 State, BodySegmentCount, UpdatedAt)
            VALUES
                ($chapterId, $chapterRevisionHash, $textProfileFingerprint, $planOutputHash,
                 $state, $bodySegmentCount, $updatedAt)
            ON CONFLICT(ChapterId) DO UPDATE SET
                ChapterRevisionHash = excluded.ChapterRevisionHash,
                TextProfileFingerprint = excluded.TextProfileFingerprint,
                PlanOutputHash = excluded.PlanOutputHash,
                State = excluded.State,
                BodySegmentCount = excluded.BodySegmentCount,
                UpdatedAt = excluded.UpdatedAt;
            """;
        AddPlanHeaderParameters(header, plan);
        header.Parameters.AddWithValue("$planOutputHash", plan.PlanOutputHash.ToArray());
        await header.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        var delete = connection.CreateCommand();
        delete.Transaction = transaction;
        delete.CommandText = "DELETE FROM ChapterSpeechPlanSegments WHERE ChapterId = $chapterId;";
        delete.Parameters.AddWithValue("$chapterId", plan.ChapterId);
        await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        foreach (var segment in plan.Segments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText =
                """
                INSERT INTO ChapterSpeechPlanSegments
                    (ChapterId, OrderIndex, SegmentKind, SourceStartOffset, SourceLength, SpeechTextHash)
                VALUES
                    ($chapterId, $orderIndex, $segmentKind, $sourceStartOffset, $sourceLength, $speechTextHash);
                """;
            insert.Parameters.AddWithValue("$chapterId", plan.ChapterId);
            insert.Parameters.AddWithValue("$orderIndex", segment.OrderIndex);
            insert.Parameters.AddWithValue("$segmentKind", (int)segment.SegmentKind);
            insert.Parameters.AddWithValue("$sourceStartOffset", segment.SourceStartOffset);
            insert.Parameters.AddWithValue("$sourceLength", segment.SourceLength);
            insert.Parameters.AddWithValue("$speechTextHash", segment.SpeechTextHash.ToArray());
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static void AddPlanHeaderParameters(
        SqliteCommand command,
        ChapterSpeechPlan plan)
    {
        command.Parameters.AddWithValue("$chapterId", plan.ChapterId);
        command.Parameters.AddWithValue("$chapterRevisionHash", plan.ChapterRevisionHash.ToArray());
        command.Parameters.AddWithValue("$textProfileFingerprint", plan.TextProfileFingerprint.Value.ToArray());
        command.Parameters.AddWithValue("$state", (int)plan.State);
        command.Parameters.AddWithValue("$bodySegmentCount", plan.BodySegmentCount);
        command.Parameters.AddWithValue("$updatedAt", SqliteDateTimeMapper.Format(plan.UpdatedAt));
    }

    private static async Task<IReadOnlyList<ChapterSpeechPlanSegment>> ReadSegmentsAsync(
        SqliteConnection connection,
        string chapterId,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT OrderIndex, SegmentKind, SourceStartOffset, SourceLength, SpeechTextHash
            FROM ChapterSpeechPlanSegments
            WHERE ChapterId = $chapterId
            ORDER BY OrderIndex;
            """;
        command.Parameters.AddWithValue("$chapterId", chapterId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        var segments = new List<ChapterSpeechPlanSegment>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            segments.Add(new ChapterSpeechPlanSegment(
                reader.GetInt32(0),
                (SpeechSegmentKind)reader.GetInt32(1),
                reader.GetInt32(2),
                reader.GetInt32(3),
                new Fingerprint(reader.GetFieldValue<byte[]>(4))));
        }

        return segments;
    }
}
