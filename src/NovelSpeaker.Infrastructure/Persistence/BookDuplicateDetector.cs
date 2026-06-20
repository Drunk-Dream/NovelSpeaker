using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Application.Books;

namespace NovelSpeaker.Infrastructure.Persistence;

/// <summary>
/// Checks for already-imported books using their source hash.
/// </summary>
public sealed class BookDuplicateDetector : IBookDuplicateDetector
{
    private readonly ISqliteConnectionFactory _connectionFactory;

    public BookDuplicateDetector(ISqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<string?> FindExistingBookIdAsync(string sourceHash, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT Id FROM Books WHERE SourceHash = $sourceHash LIMIT 1;";
        command.Parameters.AddWithValue("$sourceHash", sourceHash);
        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }
}
