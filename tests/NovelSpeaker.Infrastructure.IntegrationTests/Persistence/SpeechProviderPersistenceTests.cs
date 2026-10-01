using Microsoft.Data.Sqlite;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Application.Speech.Providers;
using NovelSpeaker.Domain.Settings;
using NovelSpeaker.Domain.Speech.Providers;
using NovelSpeaker.Infrastructure.FileSystem;
using NovelSpeaker.Infrastructure.Persistence;
using NovelSpeaker.Infrastructure.Persistence.Books;
using NovelSpeaker.Infrastructure.Persistence.Speech;
using NovelSpeaker.Infrastructure.Settings;
using NovelSpeaker.TestKit.Common;
using Xunit;

namespace NovelSpeaker.Infrastructure.IntegrationTests.Persistence;

public sealed class SpeechProviderPersistenceTests
{
    [Fact]
    public async Task Version_9_upgrade_preserves_HTTP_and_persists_empty_and_configured_Edge_singleton()
    {
        using var temporaryDirectory = new TemporaryDatabase();
        var (directories, factory) = await CreateVersion7DatabaseAsync(temporaryDirectory.Path);
        await new SqliteMigrationRunner(factory,
            SqliteMigrationRunner.AllMigrations.Where(item => item.Version == 8).ToArray()).InitializeAsync(CancellationToken.None);
        var store = new SqliteProviderStore(factory);
        // v8 has no Edge config table, so insert a valid HTTP row through the existing v8 schema.
        await using (var connection = await factory.OpenConnectionAsync(CancellationToken.None))
        {
            var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO SpeechProviders (Id, Type, Name, NameKey, SortOrder, CreatedAt, UpdatedAt)
                VALUES ('00000000-0000-0000-0000-000000000001', 1, 'HTTP', 'HTTP', 5,
                    '2026-01-01T00:00:00.0000000+00:00', '2026-01-01T00:00:00.0000000+00:00');
                INSERT INTO HttpSpeechProviderConfigs (ProviderId, UrlTemplate, Method, HeadersJson)
                VALUES ('00000000-0000-0000-0000-000000000001', 'https://example.invalid/tts', 'GET', '{}');
                """;
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }
        await new SqliteMigrationRunner(factory).InitializeAsync(CancellationToken.None);
        Assert.Equal(10, await GetSchemaVersionAsync(factory));
        var http = Assert.Single(await store.GetAllAsync(CancellationToken.None));
        Assert.Equal("HTTP", http.Name);
        Assert.Equal(5, http.SortOrder);
        var edge = new SpeechProviderInstance(ProviderId.New(), "Microsoft Edge", 6,
            new EdgeSpeechProviderConfiguration(null), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        await store.SaveAsync(edge, CancellationToken.None);
        Assert.Null(((EdgeSpeechProviderConfiguration)(await store.GetByIdAsync(edge.Id, CancellationToken.None))!.Configuration).Voice);
        var voice = new EdgeVoice("voice-A", "Friendly", "zh-CN", "Female");
        await store.SaveAsync(edge with { Configuration = new EdgeSpeechProviderConfiguration(voice) }, CancellationToken.None);
        var reloaded = await new SqliteProviderStore(factory).GetByIdAsync(edge.Id, CancellationToken.None);
        Assert.Equal(voice, ((EdgeSpeechProviderConfiguration)reloaded!.Configuration).Voice);
        Assert.Equal(edge.Id, reloaded.Id);
        await store.UpdateSortOrderAsync([edge.Id, http.Id], CancellationToken.None);
        Assert.Equal([edge.Id, http.Id], (await store.GetAllAsync(CancellationToken.None)).Select(item => item.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync(edge with { Id = ProviderId.New() }, CancellationToken.None));
        Assert.Equal(2, (await store.GetAllAsync(CancellationToken.None)).Count);
    }

    [Fact]
    public async Task Version_7_rules_migrate_to_providers_and_reconcile_current_selection()
    {
        using var temporaryDirectory = new TemporaryDatabase();
        var (directories, factory) = await CreateVersion7DatabaseAsync(temporaryDirectory.Path);
        await InsertLegacyRulesAsync(factory);
        await directories.EnsureCreatedAsync(CancellationToken.None);
        await File.WriteAllTextAsync(directories.SettingsPath, """{"SelectedTtsRuleId":10}""");

        var settingsStore = new JsonAppSettingsStore(directories);
        var settings = await settingsStore.LoadAsync(CancellationToken.None);
        using var settingsService = new AppSettingsService(settingsStore, settings);
        var providerStore = new SqliteProviderStore(factory);
        var initializer = CreateInitializer(directories, factory, settingsService, providerStore);

        await initializer.InitializeAsync(CancellationToken.None);

        var providers = await providerStore.GetAllAsync(CancellationToken.None);
        Assert.Equal(
            [
                "Alpha",
                "alpha (2)",
                "Microsoft Edge (2)",
                "Disabled",
                "Source and Java text",
                "Locally scoped source",
                "Source is a label",
                "Locally scoped loop source",
                "Source class method",
                "Source class field",
                "Locally scoped catch source",
                "Structured scalar body",
                "Raw JSON template body"
            ],
            providers.Select(provider => provider.Name));
        Assert.Equal(Enumerable.Range(0, providers.Count), providers.Select(provider => provider.SortOrder));
        Assert.Equal(ProviderId.FromLegacyHttpTtsRuleId(10), providers[0].Id);
        Assert.Equal(ProviderId.FromLegacyHttpTtsRuleId(11), providers[1].Id);
        Assert.Equal(ProviderId.FromLegacyHttpTtsRuleId(12), providers[2].Id);
        Assert.NotEqual(ProviderId.FromLegacyHttpTtsRuleId(13), providers[3].Id);
        Assert.Equal(ProviderId.FromLegacyHttpTtsRuleId(10), settingsService.Current.CurrentProviderId);

        var firstConfiguration = Assert.IsType<HttpSpeechProviderConfiguration>(providers[0].Configuration);
        Assert.Equal("POST", firstConfiguration.Method);
        Assert.Equal("application/json", firstConfiguration.Headers["Content-Type"]);
        Assert.Equal("{\"text\":\"example\"}", firstConfiguration.BodyTemplate);
        var scalarConfiguration = Assert.IsType<HttpSpeechProviderConfiguration>(
            providers.Single(provider => provider.Name == "Structured scalar body").Configuration);
        Assert.Equal("42", scalarConfiguration.BodyTemplate);
        var rawJsonConfiguration = Assert.IsType<HttpSpeechProviderConfiguration>(
            providers.Single(provider => provider.Name == "Raw JSON template body").Configuration);
        Assert.Equal("application/json; charset=utf-8", rawJsonConfiguration.Headers["Content-Type"]);

        await using var connection = await factory.OpenConnectionAsync(CancellationToken.None);
        var names = await GetTableNamesAsync(connection);
        Assert.DoesNotContain("HttpTtsRules", names);
        Assert.DoesNotContain("ProviderMigrationReports", names);
        Assert.DoesNotContain("ProviderMigrationSkippedItems", names);
        Assert.DoesNotContain("SelectedTtsRuleId", await File.ReadAllTextAsync(directories.SettingsPath));
    }

    [Fact]
    public async Task Disabled_legacy_selection_is_cleared_after_migration()
    {
        using var temporaryDirectory = new TemporaryDatabase();
        var (directories, factory) = await CreateVersion7DatabaseAsync(temporaryDirectory.Path);
        await InsertLegacyRuleAsync(factory, 31, "Disabled provider", "https://example.invalid/?text={{text}}", isEnabled: false);
        var settingsStore = new TestAppSettingsStore();
        using var settingsService = new AppSettingsService(
            settingsStore,
            AppSettings.Default with { CurrentProviderId = ProviderId.FromLegacyHttpTtsRuleId(31) });
        var providerStore = new SqliteProviderStore(factory);

        await CreateInitializer(directories, factory, settingsService, providerStore)
            .InitializeAsync(CancellationToken.None);

        Assert.Null(settingsService.Current.CurrentProviderId);
        var providers = await providerStore.GetAllAsync(CancellationToken.None);
        Assert.Single(providers);
        Assert.NotEqual(ProviderId.FromLegacyHttpTtsRuleId(31), providers[0].Id);
    }

    [Fact]
    public async Task Failed_settings_reconciliation_retries_after_database_migration_commits()
    {
        using var temporaryDirectory = new TemporaryDatabase();
        var (directories, factory) = await CreateVersion7DatabaseAsync(temporaryDirectory.Path);
        var missingLegacySelection = ProviderId.FromLegacyHttpTtsRuleId(404);
        var settingsStore = new TestAppSettingsStore(failFirstSave: true);
        using var settingsService = new AppSettingsService(
            settingsStore,
            AppSettings.Default with { CurrentProviderId = missingLegacySelection });
        var providerStore = new SqliteProviderStore(factory);
        var initializer = CreateInitializer(directories, factory, settingsService, providerStore);

        await Assert.ThrowsAsync<IOException>(() => initializer.InitializeAsync(CancellationToken.None));
        Assert.Equal(missingLegacySelection, settingsService.Current.CurrentProviderId);
        Assert.Equal(10, await GetSchemaVersionAsync(factory));

        await initializer.InitializeAsync(CancellationToken.None);

        Assert.Null(settingsService.Current.CurrentProviderId);
        Assert.Equal(2, settingsStore.SaveCount);
    }

    [Fact]
    public async Task Failed_version_8_data_migration_rolls_back_provider_tables_rows_and_version()
    {
        using var temporaryDirectory = new TemporaryDatabase();
        var (_, factory) = await CreateVersion7DatabaseAsync(temporaryDirectory.Path);
        await InsertLegacyRuleAsync(factory, 71, "Keep on rollback", "https://example.invalid/?text={{text}}", isEnabled: true);
        var migration = SqliteMigrationRunner.AllMigrations.Single(item => item.Version == 8);
        var failingMigration = migration with
        {
            ApplyDataAsync = async (connection, transaction, cancellationToken) =>
            {
                await migration.ApplyDataAsync!(connection, transaction, cancellationToken);
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = "INSERT INTO MissingMigrationTable (Value) VALUES ('fail');";
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
        };
        var runner = new SqliteMigrationRunner(factory, [failingMigration]);

        await Assert.ThrowsAsync<SqliteException>(() => runner.InitializeAsync(CancellationToken.None));

        await using var verification = await factory.OpenConnectionAsync(CancellationToken.None);
        Assert.Equal(7, await GetSchemaVersionAsync(verification));
        var tables = await GetTableNamesAsync(verification);
        Assert.Contains("HttpTtsRules", tables);
        Assert.DoesNotContain("SpeechProviders", tables);
        Assert.DoesNotContain("HttpSpeechProviderConfigs", tables);
        var ruleCount = verification.CreateCommand();
        ruleCount.CommandText = "SELECT COUNT(*) FROM HttpTtsRules WHERE Id = 71;";
        Assert.Equal(1, Convert.ToInt32(await ruleCount.ExecuteScalarAsync(CancellationToken.None)));
    }

    [Fact]
    public async Task Provider_store_persists_order_updates_and_case_insensitive_name_uniqueness()
    {
        using var temporaryDirectory = new TemporaryDatabase();
        var directories = new AppDataDirectoryProvider(temporaryDirectory.Path);
        await directories.EnsureCreatedAsync(CancellationToken.None);
        var factory = new SqliteConnectionFactory(directories);
        await new SqliteMigrationRunner(factory).InitializeAsync(CancellationToken.None);
        var store = new SqliteProviderStore(factory);
        var first = CreateProvider("First", sortOrder: 1);
        var second = CreateProvider("Second", sortOrder: 0);

        await store.SaveAsync(first, CancellationToken.None);
        await store.SaveAsync(second, CancellationToken.None);

        Assert.Equal(["Second", "First"], (await store.GetAllAsync(CancellationToken.None)).Select(provider => provider.Name));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.SaveAsync(CreateProvider("fIrSt", sortOrder: 2), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.SaveAsync(CreateProvider("Microsoft Edge", sortOrder: 2), CancellationToken.None));

        await store.SaveAsync(first with { Name = "Renamed", SortOrder = 3 }, CancellationToken.None);
        Assert.Equal(["Second", "Renamed"], (await store.GetAllAsync(CancellationToken.None)).Select(provider => provider.Name));

        await store.UpdateSortOrderAsync([first.Id, second.Id], CancellationToken.None);
        Assert.Equal(["Renamed", "Second"], (await store.GetAllAsync(CancellationToken.None)).Select(provider => provider.Name));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.UpdateSortOrderAsync(
            [second.Id, ProviderId.New()], CancellationToken.None));
        Assert.Equal(["Renamed", "Second"], (await new SqliteProviderStore(factory).GetAllAsync(CancellationToken.None)).Select(provider => provider.Name));

        var copy = first with { Id = ProviderId.New(), Name = "Copy" };
        await store.InsertAfterAsync(copy, first.Id, CancellationToken.None);
        Assert.Equal([first.Id, copy.Id, second.Id], (await store.GetAllAsync(CancellationToken.None)).Select(provider => provider.Id));
        var rejected = copy with { Id = ProviderId.New(), Name = "Another copy" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.InsertAfterAsync(rejected, ProviderId.New(), CancellationToken.None));
        Assert.Null(await store.GetByIdAsync(rejected.Id, CancellationToken.None));
        Assert.Equal([first.Id, copy.Id, second.Id], (await store.GetAllAsync(CancellationToken.None)).Select(provider => provider.Id));
        await store.DeleteAsync(copy.Id, CancellationToken.None);

        await store.DeleteAsync(second.Id, CancellationToken.None);
        Assert.Null(await store.GetByIdAsync(second.Id, CancellationToken.None));
        await using var connection = await factory.OpenConnectionAsync(CancellationToken.None);
        var configCount = connection.CreateCommand();
        configCount.CommandText = "SELECT COUNT(*) FROM HttpSpeechProviderConfigs;";
        Assert.Equal(1, Convert.ToInt32(await configCount.ExecuteScalarAsync(CancellationToken.None)));
    }

    private static async Task<(AppDataDirectoryProvider Directories, SqliteConnectionFactory Factory)> CreateVersion7DatabaseAsync(
        string root)
    {
        var directories = new AppDataDirectoryProvider(root);
        await directories.EnsureCreatedAsync(CancellationToken.None);
        var factory = new SqliteConnectionFactory(directories);
        var migrations = SqliteMigrationRunner.AllMigrations.Where(migration => migration.Version <= 7).ToArray();
        await new SqliteMigrationRunner(factory, migrations).InitializeAsync(CancellationToken.None);
        return (directories, factory);
    }

    private static StartupDatabaseInitializer CreateInitializer(
        AppDataDirectoryProvider directories,
        SqliteConnectionFactory factory,
        IAppSettingsService? settingsService = null,
        IProviderStore? providerStore = null)
    {
        var repository = new ChapterRuleRepository(factory);
        return new StartupDatabaseInitializer(
            directories,
            new SqliteMigrationRunner(factory),
            new DefaultChapterRuleSeeder(repository),
            settingsService: settingsService,
            providerStore: providerStore);
    }

    private static async Task InsertLegacyRulesAsync(SqliteConnectionFactory factory)
    {
        await InsertLegacyRuleAsync(
            factory,
            10,
            "Alpha",
            "https://example.invalid/?text={{text}}",
            isEnabled: true,
            lastUsedAt: "2026-09-20T12:00:00.0000000+00:00",
            requestOptionsJson: "{\"method\":\"POST\",\"body\":{\"text\":\"example\"}}");
        await InsertLegacyRuleAsync(
            factory,
            11,
            "alpha",
            "https://example.invalid/?text={{text}}",
            isEnabled: true,
            lastUsedAt: "2026-09-20T11:00:00.0000000+00:00");
        await InsertLegacyRuleAsync(
            factory,
            12,
            "Microsoft Edge",
            "https://example.invalid/?text={{text}}",
            isEnabled: true,
            lastUsedAt: "2026-09-20T10:00:00.0000000+00:00");
        await InsertLegacyRuleAsync(
            factory,
            13,
            "Disabled",
            "https://example.invalid/?text={{text}}",
            isEnabled: false,
            lastUsedAt: "2026-09-20T09:00:00.0000000+00:00");
        await InsertLegacyRuleAsync(
            factory,
            14,
            "Source and Java text",
            "https://example.invalid/?value={{({source:'source',java:'java'}).source}}",
            isEnabled: true,
            lastUsedAt: "2026-09-20T08:00:00.0000000+00:00");
        await InsertLegacyRuleAsync(
            factory,
            18,
            "Locally scoped source",
            "https://example.invalid/?value={{((source) => source)('value')}}",
            isEnabled: true,
            lastUsedAt: "2026-09-20T07:30:00.0000000+00:00");
        await InsertLegacyRuleAsync(
            factory,
            21,
            "Structured scalar body",
            "https://example.invalid/tts",
            isEnabled: true,
            lastUsedAt: "2026-09-20T02:00:00.0000000+00:00",
            requestOptionsJson: "{\"method\":\"POST\",\"body\":42}");
        await InsertLegacyRuleAsync(
            factory,
            23,
            "Global source before loop binding",
            "https://example.invalid/?value={{(()=> { const x = source.name; for (let source of []) {} return x; })()}}",
            isEnabled: true,
            lastUsedAt: "2026-09-20T07:20:00.0000000+00:00");
        await InsertLegacyRuleAsync(
            factory,
            33,
            "Source is a label",
            "https://example.invalid/?value={{(()=> { source: while (true) { break source; } return speakText; })()}}",
            isEnabled: true,
            lastUsedAt: "2026-09-20T07:17:00.0000000+00:00");
        await InsertLegacyRuleAsync(
            factory,
            24,
            "Locally scoped loop source",
            "https://example.invalid/?value={{(()=> { for (let source of []) { return source.name; } return text; })()}}",
            isEnabled: true,
            lastUsedAt: "2026-09-20T07:15:00.0000000+00:00");
        await InsertLegacyRuleAsync(
            factory,
            34,
            "Source class method",
            "https://example.invalid/?value={{new (class { source() { return speakText; } })().source()}}",
            isEnabled: true,
            lastUsedAt: "2026-09-20T07:12:00.0000000+00:00");
        await InsertLegacyRuleAsync(
            factory,
            35,
            "Source class field",
            "https://example.invalid/?value={{new (class { source = speakText; })().source}}",
            isEnabled: true,
            lastUsedAt: "2026-09-20T07:11:00.0000000+00:00");
        await InsertLegacyRuleAsync(
            factory,
            25,
            "Locally scoped catch source",
            "https://example.invalid/?value={{(()=> { try {} catch (source) { return source.name; } return text; })()}}",
            isEnabled: true,
            lastUsedAt: "2026-09-20T07:10:00.0000000+00:00");
        await InsertLegacyRuleAsync(
            factory,
            26,
            "Global source in switch discriminant",
            "https://example.invalid/?value={{(()=> { switch (source.name) { case 'x': let source; break; } return text; })()}}",
            isEnabled: true,
            lastUsedAt: "2026-09-20T07:05:00.0000000+00:00");
        await InsertLegacyRuleAsync(
            factory,
            15,
            "Removed source global",
            "https://example.invalid/?value={{source.name}}",
            isEnabled: true,
            lastUsedAt: "2026-09-20T07:00:00.0000000+00:00");
        await InsertLegacyRuleAsync(
            factory,
            16,
            "Removed java global",
            "https://example.invalid/?value={{java.encodeURI(text)}}",
            isEnabled: true,
            lastUsedAt: "2026-09-20T06:00:00.0000000+00:00");
        await InsertLegacyRuleAsync(
            factory,
            17,
            "Corrupt headers",
            "https://example.invalid/?text={{text}}",
            isEnabled: true,
            headersJson: "not json",
            lastUsedAt: "2026-09-20T05:00:00.0000000+00:00");
        await InsertLegacyRuleAsync(
            factory,
            19,
            "Malformed template expression",
            "https://example.invalid/?value={{source + }}",
            isEnabled: true,
            lastUsedAt: "2026-09-20T04:00:00.0000000+00:00");
        await InsertLegacyRuleAsync(
            factory,
            20,
            "Unshadowed source reference",
            "https://example.invalid/?value={{(() => { const source = 'local'; return text; })() + source.name}}",
            isEnabled: true,
            lastUsedAt: "2026-09-20T03:00:00.0000000+00:00");
        await InsertLegacyRuleAsync(
            factory,
            22,
            "Destructured source global",
            "https://example.invalid/?value={{(({source} = {}), text)}}",
            isEnabled: true,
            lastUsedAt: "2026-09-20T01:00:00.0000000+00:00");
        await InsertLegacyRuleAsync(
            factory,
            27,
            "Malformed JSON body",
            "https://example.invalid/tts",
            isEnabled: true,
            lastUsedAt: "2026-09-20T00:50:00.0000000+00:00",
            headersJson: "{\"Content-Type\":\"application/json\"}",
            requestOptionsJson: "{\"method\":\"POST\",\"body\":\"not-json\"}");
        await InsertLegacyRuleAsync(
            factory,
            28,
            "Malformed templated JSON body",
            "https://example.invalid/tts",
            isEnabled: true,
            lastUsedAt: "2026-09-20T00:40:00.0000000+00:00",
            headersJson: "{\"Content-Type\":\"application/json; charset=utf-8\"}",
            requestOptionsJson: "{\"method\":\"POST\",\"body\":\"{\\\"text\\\": {{text}}\"}");
        await InsertLegacyRuleAsync(
            factory,
            29,
            "Form body with optional whitespace",
            "https://example.invalid/tts",
            isEnabled: true,
            lastUsedAt: "2026-09-20T00:30:00.0000000+00:00",
            headersJson: "{\"Content-Type\":\" application/x-www-form-urlencoded\"}",
            requestOptionsJson: "{\"method\":\"POST\",\"body\":{\"text\":\"{{text}}\"}}");
        await InsertLegacyRuleAsync(
            factory,
            30,
            "Global source in for-of right side",
            "https://example.invalid/?value={{(()=> { for (let source of source.items) {} return text; })()}}",
            isEnabled: true,
            lastUsedAt: "2026-09-20T00:20:00.0000000+00:00");
        await InsertLegacyRuleAsync(
            factory,
            32,
            "Raw JSON template body",
            "https://example.invalid/tts",
            isEnabled: true,
            lastUsedAt: "2026-09-20T00:10:00.0000000+00:00",
            headersJson: "{\"Content-Type\":\"application/json; charset=utf-8\"}",
            requestOptionsJson: "{\"method\":\"POST\",\"body\":\"{\\\"text\\\":{{JSON.stringify(speakText)}}}\"}");
        await InsertLegacyRuleAsync(
            factory,
            31,
            "Raw text in JSON body",
            "https://example.invalid/tts",
            isEnabled: true,
            lastUsedAt: "2026-09-20T00:05:00.0000000+00:00",
            headersJson: "{\"Content-Type\":\"application/json\"}",
            requestOptionsJson: "{\"method\":\"POST\",\"body\":\"{{speakText}}\"}");
    }

    private static async Task InsertLegacyRuleAsync(
        SqliteConnectionFactory factory,
        long id,
        string name,
        string url,
        bool isEnabled,
        string? lastUsedAt = null,
        string? headersJson = null,
        string? requestOptionsJson = null)
    {
        await using var connection = await factory.OpenConnectionAsync(CancellationToken.None);
        var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO HttpTtsRules
                (Id, Name, Url, ConcurrentRate, Header, RequestOptionsJson, IsEnabled, LastUsedAt, CreatedAt, UpdatedAt)
            VALUES
                ($id, $name, $url, NULL, $headers, $options, $enabled, $lastUsedAt, $now, $now);
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$url", url);
        command.Parameters.AddWithValue("$headers", (object?)headersJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$options", (object?)requestOptionsJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$enabled", isEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$lastUsedAt", (object?)lastUsedAt ?? DBNull.Value);
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(CancellationToken.None);
    }

    private static async Task<IReadOnlyList<string>> GetTableNamesAsync(SqliteConnection connection)
    {
        var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table';";
        await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);
        var names = new List<string>();
        while (await reader.ReadAsync(CancellationToken.None))
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    private static async Task<int> GetSchemaVersionAsync(SqliteConnectionFactory factory)
    {
        await using var connection = await factory.OpenConnectionAsync(CancellationToken.None);
        return await GetSchemaVersionAsync(connection);
    }

    private static async Task<int> GetSchemaVersionAsync(SqliteConnection connection)
    {
        var command = connection.CreateCommand();
        command.CommandText = "SELECT MAX(Version) FROM SchemaVersion;";
        return Convert.ToInt32(await command.ExecuteScalarAsync(CancellationToken.None));
    }

    private static SpeechProviderInstance CreateProvider(string name, int sortOrder) =>
        new(
            ProviderId.New(),
            name,
            sortOrder,
            new HttpSpeechProviderConfiguration(
                "https://example.invalid/?text={{text}}",
                "GET",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                null,
                null),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

    private sealed class TestAppSettingsStore(bool failFirstSave = false) : IAppSettingsStore
    {
        private bool _failFirstSave = failFirstSave;

        public int SaveCount { get; private set; }

        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(AppSettings.Default);

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SaveCount++;
            if (_failFirstSave)
            {
                _failFirstSave = false;
                throw new IOException("Simulated settings persistence failure.");
            }

            return Task.CompletedTask;
        }
    }

    private sealed class TemporaryDatabase : IDisposable
    {
        private readonly TemporaryDirectory _directory = new("NovelSpeakerProviderPersistenceTests");

        public string Path => _directory.Path;

        public void Dispose()
        {
            using var connection = new SqliteConnection($"Data Source={System.IO.Path.Combine(Path, "app.db")}");
            connection.Open();
            SqliteConnection.ClearPool(connection);
            connection.Close();
            _directory.Dispose();
        }
    }
}
