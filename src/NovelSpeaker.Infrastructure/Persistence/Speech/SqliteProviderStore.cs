using System.Text.Json;
using Microsoft.Data.Sqlite;
using NovelSpeaker.Application.Speech.Providers;
using NovelSpeaker.Domain.Speech.Providers;
using NovelSpeaker.Infrastructure.Persistence;

namespace NovelSpeaker.Infrastructure.Persistence.Speech;

public sealed class SqliteProviderStore : IProviderStore
{
    private static readonly JsonSerializerOptions JsonOptions = new();
    private const string ProviderSelect =
        """
        SELECT p.Id,
               p.Type,
               p.Name,
               p.SortOrder,
               p.CreatedAt,
               p.UpdatedAt,
               c.UrlTemplate,
               c.Method,
               c.HeadersJson,
               c.BodyTemplate,
               c.MaxRequests,
               c.WindowMilliseconds,
               e.ProviderId, e.VoiceId, e.FriendlyName, e.Locale, e.Gender
        FROM SpeechProviders p
        LEFT JOIN HttpSpeechProviderConfigs c ON c.ProviderId = p.Id
        LEFT JOIN EdgeSpeechProviderConfigs e ON e.ProviderId = p.Id
        """;

    private readonly ISqliteConnectionFactory _connectionFactory;

    public SqliteProviderStore(ISqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public async Task<IReadOnlyList<SpeechProviderInstance>> GetAllAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        return await ReadAllAsync(connection, null, cancellationToken);
    }

    internal static async Task<IReadOnlyList<SpeechProviderInstance>> ReadAllAsync(
        SqliteConnection connection, SqliteTransaction? transaction, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = ProviderSelect + " ORDER BY p.SortOrder, p.Id;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var providers = new List<SpeechProviderInstance>();
        while (await reader.ReadAsync(cancellationToken))
        {
            if (TryReadProvider(reader, out var provider))
            {
                providers.Add(provider);
            }
        }

        return providers;
    }

    public async Task<SpeechProviderInstance?> GetByIdAsync(
        ProviderId providerId,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = ProviderSelect + " WHERE p.Id = $id;";
        command.Parameters.AddWithValue("$id", providerId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) && TryReadProvider(reader, out var provider)
            ? provider
            : null;
    }

    public Task SaveAsync(SpeechProviderInstance provider, CancellationToken cancellationToken) =>
        SaveCoreAsync(provider, null, cancellationToken);

    public Task InsertAfterAsync(SpeechProviderInstance provider, ProviderId precedingId, CancellationToken cancellationToken) =>
        SaveCoreAsync(provider, precedingId, cancellationToken);

    private async Task SaveCoreAsync(SpeechProviderInstance provider, ProviderId? precedingId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentOutOfRangeException.ThrowIfEqual(provider.Id.Value, Guid.Empty);
        var configuration = provider.Configuration as HttpSpeechProviderConfiguration;
        var isEmptyEdge = provider.Configuration is EdgeSpeechProviderConfiguration { Voice: null } &&
            provider.Name == SpeechProviderNameRules.MicrosoftEdgeName;
        if (!SpeechProviderNameRules.TryNormalize(provider.Name, out var name) ||
            (!isEmptyEdge && !ProviderConfigurationValidator.Validate(provider with { Name = name }).IsValid))
        {
            throw new InvalidOperationException("Provider 名称或配置无效。");
        }

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            List<string>? insertionOrder = null;
            if (precedingId is not null)
            {
                insertionOrder = [];
                await using (var orderCommand = connection.CreateCommand())
                {
                    orderCommand.Transaction = transaction;
                    orderCommand.CommandText = "SELECT Id FROM SpeechProviders ORDER BY SortOrder, Id;";
                    await using var reader = await orderCommand.ExecuteReaderAsync(cancellationToken);
                    while (await reader.ReadAsync(cancellationToken)) insertionOrder.Add(reader.GetString(0));
                }
                var sourceIndex = insertionOrder.IndexOf(precedingId.Value.ToString());
                if (sourceIndex < 0 || insertionOrder.Contains(provider.Id.ToString()))
                {
                    throw new InvalidOperationException("语音服务列表已变化，请刷新后重试。");
                }
                provider = provider with { SortOrder = sourceIndex + 1 };
                insertionOrder.Insert(sourceIndex + 1, provider.Id.ToString());
            }

            await using (var providerCommand = connection.CreateCommand())
            {
                providerCommand.Transaction = transaction;
                providerCommand.CommandText =
                    """
                    INSERT INTO SpeechProviders (Id, Type, Name, NameKey, SortOrder, CreatedAt, UpdatedAt)
                    VALUES ($id, $type, $name, $nameKey, $sortOrder, $createdAt, $updatedAt)
                    ON CONFLICT(Id) DO UPDATE SET
                        Type = excluded.Type,
                        Name = excluded.Name,
                        NameKey = excluded.NameKey,
                        SortOrder = excluded.SortOrder,
                        UpdatedAt = excluded.UpdatedAt;
                    """;
                providerCommand.Parameters.AddWithValue("$id", provider.Id.ToString());
                providerCommand.Parameters.AddWithValue("$type", (int)provider.Type);
                providerCommand.Parameters.AddWithValue("$name", name);
                providerCommand.Parameters.AddWithValue("$nameKey", name.ToUpperInvariant());
                providerCommand.Parameters.AddWithValue("$sortOrder", provider.SortOrder);
                providerCommand.Parameters.AddWithValue("$createdAt", SqliteDateTimeMapper.Format(provider.CreatedAt));
                providerCommand.Parameters.AddWithValue("$updatedAt", SqliteDateTimeMapper.Format(provider.UpdatedAt));
                await providerCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            if (provider.Configuration is EdgeSpeechProviderConfiguration edge)
            {
                await using var edgeCommand = connection.CreateCommand();
                edgeCommand.Transaction = transaction;
                edgeCommand.CommandText = """
                    INSERT INTO EdgeSpeechProviderConfigs (ProviderId, VoiceId, FriendlyName, Locale, Gender)
                    VALUES ($id, $voice, $friendly, $locale, $gender)
                    ON CONFLICT(ProviderId) DO UPDATE SET VoiceId = excluded.VoiceId,
                        FriendlyName = excluded.FriendlyName, Locale = excluded.Locale, Gender = excluded.Gender;
                    """;
                edgeCommand.Parameters.AddWithValue("$id", provider.Id.ToString());
                edgeCommand.Parameters.AddWithValue("$voice", (object?)edge.Voice?.VoiceId ?? DBNull.Value);
                edgeCommand.Parameters.AddWithValue("$friendly", (object?)edge.Voice?.FriendlyName ?? DBNull.Value);
                edgeCommand.Parameters.AddWithValue("$locale", (object?)edge.Voice?.Locale ?? DBNull.Value);
                edgeCommand.Parameters.AddWithValue("$gender", (object?)edge.Voice?.Gender ?? DBNull.Value);
                await edgeCommand.ExecuteNonQueryAsync(cancellationToken);
            }
            else if (configuration is not null)
            {
                await using var configurationCommand = connection.CreateCommand();
                configurationCommand.Transaction = transaction;
                configurationCommand.CommandText =
                    """
                    INSERT INTO HttpSpeechProviderConfigs (
                        ProviderId, UrlTemplate, Method, HeadersJson, BodyTemplate, MaxRequests, WindowMilliseconds)
                    VALUES ($providerId, $urlTemplate, $method, $headersJson, $bodyTemplate, $maxRequests, $windowMilliseconds)
                    ON CONFLICT(ProviderId) DO UPDATE SET
                        UrlTemplate = excluded.UrlTemplate,
                        Method = excluded.Method,
                        HeadersJson = excluded.HeadersJson,
                        BodyTemplate = excluded.BodyTemplate,
                        MaxRequests = excluded.MaxRequests,
                        WindowMilliseconds = excluded.WindowMilliseconds;
                    """;
                configurationCommand.Parameters.AddWithValue("$providerId", provider.Id.ToString());
                configurationCommand.Parameters.AddWithValue("$urlTemplate", configuration.UrlTemplate);
                configurationCommand.Parameters.AddWithValue("$method", configuration.Method);
                configurationCommand.Parameters.AddWithValue("$headersJson", JsonSerializer.Serialize(configuration.Headers, JsonOptions));
                configurationCommand.Parameters.AddWithValue("$bodyTemplate", (object?)configuration.BodyTemplate ?? DBNull.Value);
                configurationCommand.Parameters.AddWithValue("$maxRequests", (object?)configuration.RateLimit?.MaxRequests ?? DBNull.Value);
                configurationCommand.Parameters.AddWithValue("$windowMilliseconds", (object?)configuration.RateLimit?.WindowMilliseconds ?? DBNull.Value);
                await configurationCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            if (insertionOrder is not null)
            {
                await WriteSortOrderAsync(connection, transaction, insertionOrder, cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            throw new InvalidOperationException("Provider 名称必须全局唯一（忽略大小写）。", exception);
        }
    }

    public async Task UpdateSortOrderAsync(IReadOnlyList<ProviderId> orderedIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(orderedIds);
        if (orderedIds.Distinct().Count() != orderedIds.Count)
        {
            throw new ArgumentException("Provider order contains duplicate IDs.", nameof(orderedIds));
        }

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await WriteSortOrderAsync(connection, transaction, orderedIds.Select(id => id.ToString()).ToArray(), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task WriteSortOrderAsync(SqliteConnection connection, SqliteTransaction transaction,
        IReadOnlyList<string> orderedIds, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE SpeechProviders SET SortOrder = $order WHERE Id = $id;";
        var idParameter = command.Parameters.Add("$id", SqliteType.Text);
        var orderParameter = command.Parameters.Add("$order", SqliteType.Integer);
        for (var index = 0; index < orderedIds.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            idParameter.Value = orderedIds[index];
            orderParameter.Value = index;
            if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw new InvalidOperationException("语音服务列表已变化，请刷新后重试。");
            }
        }
    }

    public async Task DeleteAsync(ProviderId providerId, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM SpeechProviders WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", providerId.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static bool TryReadProvider(SqliteDataReader reader, out SpeechProviderInstance provider)
    {
        provider = null!;
        try
        {
            if (!Guid.TryParse(reader.GetString(0), out var id) ||
                !SqliteDateTimeMapper.TryParse(reader.GetString(4), out var createdAt) ||
                !SqliteDateTimeMapper.TryParse(reader.GetString(5), out var updatedAt))
            {
                return false;
            }

            if (reader.GetInt32(1) == (int)SpeechProviderType.MicrosoftEdge)
            {
                if (reader.IsDBNull(12) || reader.GetString(2) != SpeechProviderNameRules.MicrosoftEdgeName) return false;
                EdgeVoice? voice = reader.IsDBNull(13) ? null : new EdgeVoice(reader.GetString(13),
                    reader.IsDBNull(14) ? string.Empty : reader.GetString(14),
                    reader.IsDBNull(15) ? string.Empty : reader.GetString(15),
                    reader.IsDBNull(16) ? string.Empty : reader.GetString(16));
                provider = new SpeechProviderInstance(new ProviderId(id), reader.GetString(2), reader.GetInt32(3),
                    new EdgeSpeechProviderConfiguration(voice), createdAt, updatedAt);
                return true;
            }
            if (reader.GetInt32(1) != (int)SpeechProviderType.Http ||
                reader.IsDBNull(6) || reader.IsDBNull(7) || reader.IsDBNull(8)) return false;

            var headers = JsonSerializer.Deserialize<Dictionary<string, string>>(reader.GetString(8), JsonOptions);
            if (headers is null)
            {
                return false;
            }

            ProviderRequestRateLimit? rateLimit = null;
            if (reader.IsDBNull(10) != reader.IsDBNull(11))
            {
                return false;
            }

            if (!reader.IsDBNull(10))
            {
                var maxRequests = reader.GetInt32(10);
                var windowMilliseconds = reader.GetInt32(11);
                if (maxRequests <= 0 || windowMilliseconds <= 0)
                {
                    return false;
                }

                rateLimit = new ProviderRequestRateLimit(maxRequests, windowMilliseconds);
            }

            var candidate = new SpeechProviderInstance(
                new ProviderId(id),
                reader.GetString(2),
                reader.GetInt32(3),
                new HttpSpeechProviderConfiguration(
                    reader.GetString(6),
                    reader.GetString(7),
                    new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase),
                    reader.IsDBNull(9) ? null : reader.GetString(9),
                    rateLimit),
                createdAt,
                updatedAt);
            if (!ProviderConfigurationValidator.Validate(candidate).IsValid)
            {
                return false;
            }

            provider = candidate;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or InvalidCastException or JsonException or OverflowException)
        {
            return false;
        }
    }
}
