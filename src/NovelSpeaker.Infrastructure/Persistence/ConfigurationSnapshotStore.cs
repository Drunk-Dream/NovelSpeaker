using System.Text.Json;
using Microsoft.Data.Sqlite;
using NovelSpeaker.Application.Configuration;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Domain.Books;
using NovelSpeaker.Domain.Settings;
using NovelSpeaker.Domain.Speech.Providers;
using NovelSpeaker.Infrastructure.Persistence.Speech;

namespace NovelSpeaker.Infrastructure.Persistence;

/// <summary>One transaction for existing configuration tables, compensated settings-file replacement.</summary>
public sealed class ConfigurationSnapshotStore(ISqliteConnectionFactory connections, IAppSettingsStore settingsStore)
    : IConfigurationSnapshotStore
{
    public async Task<ConfigurationSnapshot> ReadAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenConnectionAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(deferred: true);
        var providers = await SqliteProviderStore.ReadAllAsync(connection, transaction, cancellationToken);
        var chapters = await ChapterRuleRepository.ReadAllAsync(connection, transaction, cancellationToken);
        var regex = await RegexReplacementRuleRepository.ReadAllAsync(connection, transaction, cancellationToken);
        var filenames = await MetadataRuleTable.ReadAllAsync(connection, transaction, "FileNameMetadataRules", cancellationToken);
        var headers = await MetadataRuleTable.ReadAllAsync(connection, transaction, "TextHeaderMetadataRules", cancellationToken);
        // Normal readers isolate malformed history. A full backup must never silently omit it.
        foreach (var (table, count) in new[] { ("SpeechProviders", providers.Count), ("ChapterRules", chapters.Count),
            ("RegexReplacementRules", regex.Count), ("FileNameMetadataRules", filenames.Count), ("TextHeaderMetadataRules", headers.Count) })
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"SELECT COUNT(*) FROM {table};";
            if (Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) != count)
                throw new InvalidOperationException("现有配置包含无效记录，无法创建完整备份。");
        }
        return new ConfigurationSnapshot(settings, providers, chapters, regex,
            filenames.Select(row => new FileNameMetadataRule(row.Id, row.Name, row.Pattern, row.SortOrder, row.IsEnabled, row.CreatedAt, row.UpdatedAt)).ToArray(),
            headers.Select(row => new TextHeaderMetadataRule(row.Id, row.Name, row.Pattern, row.SortOrder, row.IsEnabled, row.CreatedAt, row.UpdatedAt)).ToArray());
    }

    public async Task ReplaceAsync(ConfigurationSnapshot snapshot, AppSettings previousSettings, CancellationToken cancellationToken)
    {
        // Also validate callers of the persistence port before opening a write transaction.
        _ = ConfigurationBackupCodec.Write(snapshot);
        await using var connection = await connections.OpenConnectionAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        var settingsAttempted = false;
        try
        {
            await ExecuteAsync(connection, transaction, """
                DELETE FROM HttpSpeechProviderConfigs;
                DELETE FROM EdgeSpeechProviderConfigs;
                DELETE FROM SpeechProviders;
                DELETE FROM ChapterRules;
                DELETE FROM RegexReplacementRules;
                DELETE FROM FileNameMetadataRules;
                DELETE FROM TextHeaderMetadataRules;
                """, cancellationToken);
            foreach (var provider in snapshot.Providers)
            {
                await ExecuteAsync(connection, transaction, """
                    INSERT INTO SpeechProviders (Id, Type, Name, NameKey, SortOrder, CreatedAt, UpdatedAt)
                    VALUES ($id, $type, $name, $key, $sort, $created, $updated);
                    """, cancellationToken, ("$id", provider.Id.ToString()), ("$type", (int)provider.Type),
                    ("$name", provider.Name), ("$key", provider.Name.ToUpperInvariant()), ("$sort", provider.SortOrder),
                    ("$created", SqliteDateTimeMapper.Format(provider.CreatedAt)), ("$updated", SqliteDateTimeMapper.Format(provider.UpdatedAt)));
                if (provider.Configuration is HttpSpeechProviderConfiguration http)
                    await ExecuteAsync(connection, transaction, """
                        INSERT INTO HttpSpeechProviderConfigs
                            (ProviderId, UrlTemplate, Method, HeadersJson, BodyTemplate, MaxRequests, WindowMilliseconds)
                        VALUES ($id, $url, $method, $headers, $body, $max, $window);
                        """, cancellationToken, ("$id", provider.Id.ToString()), ("$url", http.UrlTemplate),
                        ("$method", http.Method), ("$headers", JsonSerializer.Serialize(http.Headers)), ("$body", http.BodyTemplate),
                        ("$max", http.RateLimit?.MaxRequests), ("$window", http.RateLimit?.WindowMilliseconds));
                else if (provider.Configuration is EdgeSpeechProviderConfiguration edge)
                    await ExecuteAsync(connection, transaction, """
                        INSERT INTO EdgeSpeechProviderConfigs (ProviderId, VoiceId, FriendlyName, Locale, Gender)
                        VALUES ($id, $voice, $friendly, $locale, $gender);
                        """, cancellationToken, ("$id", provider.Id.ToString()), ("$voice", edge.Voice?.VoiceId),
                        ("$friendly", edge.Voice?.FriendlyName), ("$locale", edge.Voice?.Locale), ("$gender", edge.Voice?.Gender));
            }
            foreach (var rule in snapshot.ChapterRules)
                await InsertRuleAsync(connection, transaction, "ChapterRules", rule.Id, rule.Name, rule.Pattern,
                    rule.SortOrder, rule.IsEnabled, rule.CreatedAt, rule.UpdatedAt, cancellationToken);
            foreach (var rule in snapshot.FileNameMetadataRules)
                await InsertRuleAsync(connection, transaction, "FileNameMetadataRules", rule.Id, rule.Name, rule.Pattern,
                    rule.SortOrder, rule.IsEnabled, rule.CreatedAt, rule.UpdatedAt, cancellationToken);
            foreach (var rule in snapshot.TextHeaderMetadataRules)
                await InsertRuleAsync(connection, transaction, "TextHeaderMetadataRules", rule.Id, rule.Name, rule.Pattern,
                    rule.SortOrder, rule.IsEnabled, rule.CreatedAt, rule.UpdatedAt, cancellationToken);
            foreach (var rule in snapshot.RegexReplacementRules)
                await ExecuteAsync(connection, transaction, """
                    INSERT INTO RegexReplacementRules (Id, Name, Pattern, Replacement, Scope, SortOrder, IsEnabled, CreatedAt, UpdatedAt)
                    VALUES ($id, $name, $pattern, $replacement, $scope, $sort, $enabled, $created, $updated);
                    """, cancellationToken, ("$id", rule.Id.ToString("D")), ("$name", rule.Name), ("$pattern", rule.Pattern),
                    ("$replacement", rule.Replacement), ("$scope", rule.Scope.ToString()), ("$sort", rule.SortOrder),
                    ("$enabled", rule.IsEnabled ? 1 : 0), ("$created", SqliteDateTimeMapper.Format(rule.CreatedAt)),
                    ("$updated", SqliteDateTimeMapper.Format(rule.UpdatedAt)));

            cancellationToken.ThrowIfCancellationRequested();
            settingsAttempted = true;
            await settingsStore.SaveAsync(snapshot.Settings, cancellationToken).ConfigureAwait(false);
            // Complete the commit once the atomic settings replacement succeeds, even if the page leaves.
            await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            try { await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false); }
            finally
            {
                if (settingsAttempted)
                    await settingsStore.SaveAsync(previousSettings, CancellationToken.None).ConfigureAwait(false);
            }
            throw;
        }
    }

    private static Task InsertRuleAsync(SqliteConnection connection, SqliteTransaction transaction, string table,
        string id, string name, string pattern, int sort, bool enabled, DateTimeOffset created, DateTimeOffset updated,
        CancellationToken cancellationToken) => ExecuteAsync(connection, transaction, $"""
            INSERT INTO {table} (Id, Name, Pattern, SortOrder, IsEnabled, CreatedAt, UpdatedAt)
            VALUES ($id, $name, $pattern, $sort, $enabled, $created, $updated);
            """, cancellationToken, ("$id", id), ("$name", name), ("$pattern", pattern), ("$sort", sort),
            ("$enabled", enabled ? 1 : 0), ("$created", SqliteDateTimeMapper.Format(created)), ("$updated", SqliteDateTimeMapper.Format(updated)));

    private static async Task ExecuteAsync(SqliteConnection connection, SqliteTransaction transaction, string sql,
        CancellationToken cancellationToken, params (string Name, object? Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
