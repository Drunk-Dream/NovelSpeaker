using Microsoft.Data.Sqlite;
using NovelSpeaker.Application.Abstractions;

namespace NovelSpeaker.Infrastructure.Persistence;

/// <summary>
/// Completes the v7 cache reset by deleting only the application-owned legacy TTS directory.
/// </summary>
public sealed class AudioCacheFormatResetService
{
    private const string ResetMarkerKey = "AudioCacheV7ResetPending";
    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly IAppDataDirectoryProvider _directories;
    private readonly IAppStoragePathResolver _pathResolver;

    public AudioCacheFormatResetService(
        ISqliteConnectionFactory connectionFactory,
        IAppDataDirectoryProvider directories,
        IAppStoragePathResolver pathResolver)
    {
        _connectionFactory = connectionFactory;
        _directories = directories;
        _pathResolver = pathResolver;
    }

    public async Task ResetIfPendingAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!await IsPendingAsync(connection, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        var cacheRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_directories.CacheDirectoryPath));
        var dataRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_directories.RootDirectoryPath));
        var legacyTtsRoot = _pathResolver.ResolvePath("Cache/Tts");
        var dataPrefix = dataRoot + Path.DirectorySeparatorChar;
        var cachePrefix = cacheRoot + Path.DirectorySeparatorChar;
        if (!legacyTtsRoot.StartsWith(dataPrefix, PathComparison) ||
            !legacyTtsRoot.StartsWith(cachePrefix, PathComparison))
        {
            throw new InvalidDataException("旧音频缓存目录不属于应用数据根目录。");
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (Directory.Exists(legacyTtsRoot))
        {
            Directory.Delete(legacyTtsRoot, recursive: true);
        }

        var marker = connection.CreateCommand();
        marker.CommandText =
            "UPDATE AppMetadata SET Value = '0' WHERE Key = $key AND Value = '1';";
        marker.Parameters.AddWithValue("$key", ResetMarkerKey);
        await marker.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> IsPendingAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = "SELECT Value FROM AppMetadata WHERE Key = $key LIMIT 1;";
        command.Parameters.AddWithValue("$key", ResetMarkerKey);
        return string.Equals(
            Convert.ToString(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)),
            "1",
            StringComparison.Ordinal);
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
}
