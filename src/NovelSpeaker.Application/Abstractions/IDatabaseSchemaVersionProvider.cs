namespace NovelSpeaker.Application.Abstractions;

/// <summary>
/// Reads the persisted database schema version without exposing the storage technology.
/// </summary>
public interface IDatabaseSchemaVersionProvider
{
    Task<int> GetCurrentVersionAsync(CancellationToken cancellationToken);
}
