using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Books.TextProcessing;
using NovelSpeaker.Application.Configuration;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Application.Speech.Providers;
using NovelSpeaker.Domain.Books;
using NovelSpeaker.Domain.Settings;
using NovelSpeaker.Domain.Speech.Providers;
using NovelSpeaker.Infrastructure.FileSystem;
using NovelSpeaker.Infrastructure.Persistence;
using NovelSpeaker.Infrastructure.Persistence.Speech;
using NovelSpeaker.Infrastructure.Settings;
using NovelSpeaker.TestKit.Common;
using Xunit;

namespace NovelSpeaker.Infrastructure.IntegrationTests;

public sealed class ConfigurationBackupTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Private_backup_roundtrips_complete_configuration_and_notifies_runtime(bool configuredEdge)
    {
        using var fixture = await Fixture.CreateAsync();
        var expected = CreateSnapshot(configuredEdge);
        await fixture.RestoreAsync(expected);
        var json = await fixture.Backup.CreateBackupAsync(CancellationToken.None);
        await fixture.RestoreAsync(CreateSnapshot(false) with
        {
            Settings = AppSettings.Default.Normalize(),
            Providers = [],
            ChapterRules = [],
            RegexReplacementRules = [],
            FileNameMetadataRules = [],
            TextHeaderMetadataRules = []
        });
        var settingsChanges = new List<AppSettingsChangedEventArgs>();
        var providerChanges = new List<SpeechProvidersChangedEventArgs>();
        var regexChanges = new List<RegexReplacementRulesChangedEventArgs>();
        fixture.Settings.Changed += (_, change) => settingsChanges.Add(change);
        fixture.CurrentProvider.Changed += (_, change) => providerChanges.Add(change);
        fixture.RegexRules.Changed += (_, change) => regexChanges.Add(change);
        await fixture.Backup.RestoreAsync(await fixture.Backup.PrepareRestoreAsync(json, CancellationToken.None), CancellationToken.None);

        Assert.Equal(json, await fixture.Backup.CreateBackupAsync(CancellationToken.None));
        Assert.Equal(expected.Settings.CurrentProviderId, fixture.Settings.Current.CurrentProviderId);
        var restored = ConfigurationBackupCodec.Read(json);
        Assert.Equal(expected.Providers.Select(provider => provider.Id), restored.Providers.Select(provider => provider.Id));
        Assert.Equal(expected.Providers.Select(provider => provider.SortOrder), restored.Providers.Select(provider => provider.SortOrder));
        var http = Assert.IsType<HttpSpeechProviderConfiguration>(restored.Providers[1].Configuration);
        Assert.Equal("Bearer private-token", http.Headers["Authorization"]);
        Assert.Equal("private-cookie", http.Headers["Cookie"]);
        Assert.Contains("private-api-key", http.UrlTemplate);
        Assert.Contains("private-body-secret", http.BodyTemplate);
        Assert.Equal(expected.ChapterRules, restored.ChapterRules);
        Assert.Equal(expected.RegexReplacementRules, restored.RegexReplacementRules);
        Assert.Equal(expected.FileNameMetadataRules, restored.FileNameMetadataRules);
        Assert.Equal(expected.TextHeaderMetadataRules, restored.TextHeaderMetadataRules);
        Assert.Equal(expected.Settings.Theme, Assert.Single(settingsChanges).Current.Theme);
        Assert.True(settingsChanges[0].IsSnapshotReplacement);
        Assert.Contains(providerChanges, change => change.AffectsSynthesis);
        Assert.True(Assert.Single(regexChanges).AffectsSpeechProfile);
    }

    [Fact]
    public async Task Backup_and_restore_leave_all_non_configuration_rows_and_files_unchanged()
    {
        using var fixture = await Fixture.CreateAsync();
        await Books.SourceBookFixture.SaveAsync(fixture.Connections, "book", ["private-chapter-title"], "Books/book.txt",
            title: "private-book-title", author: "author", chapterIds: ["chapter"]);
        await fixture.ExecuteAsync("""
            INSERT INTO ReadingProgress VALUES ('book', 0, 3, 12, 1000, 'time');
            INSERT INTO ChapterSpeechPlans VALUES ('chapter', x'01', x'02', x'03', 1, 1, 'time');
            INSERT INTO ChapterSpeechPlanSegments VALUES ('chapter', 0, 1, 0, 10, x'04');
            INSERT INTO SynthesisProfiles VALUES (x'05', 1, 1, x'06', 50, '{}', 'time');
            INSERT INTO AudioCacheEntries (CacheKey, BookId, ChapterId, SpeechTextHash, SynthesisProfileFingerprint, FilePath, FileSize, CreatedAt, LastAccessedAt)
            VALUES (x'07', 'book', 'chapter', x'04', x'05', 'Cache/audio.mp3', 10, 'time', 'time');
            """);
        var fileContents = new Dictionary<string, string>
        {
            ["Books/book.txt"] = "private-novel-body",
            ["Cache/audio.mp3"] = "private-audio",
            ["Diagnostics/session.nsdiag"] = "private-diagnostic",
            ["Logs/log.jsonl"] = "private-log",
            ["Telemetry/history.jsonl"] = "private-telemetry",
            ["Temp/temporary.txt"] = "private-temp"
        };
        foreach (var (relative, content) in fileContents)
        {
            var path = Path.Combine(fixture.Directories.RootDirectoryPath, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, content);
        }
        var before = await fixture.ReadOtherRowsAsync();
        var json = await fixture.Backup.CreateBackupAsync(CancellationToken.None);
        Assert.DoesNotContain("private-", json);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(["format", "schemaVersion", "settings", "providers", "chapterRules", "regexReplacementRules", "fileNameMetadataRules", "textHeaderMetadataRules"],
            document.RootElement.EnumerateObject().Select(property => property.Name));
        await fixture.RestoreAsync(CreateSnapshot(true));
        Assert.Equal(before, await fixture.ReadOtherRowsAsync());
        foreach (var (relative, content) in fileContents)
            Assert.Equal(content, await File.ReadAllTextAsync(Path.Combine(fixture.Directories.RootDirectoryPath, relative)));
    }

    [Fact]
    public async Task Invalid_backup_never_writes_or_publishes()
    {
        using var fixture = await Fixture.CreateAsync();
        var before = await fixture.Backup.CreateBackupAsync(CancellationToken.None);
        var notified = false;
        fixture.Settings.Changed += (_, _) => notified = true;
        foreach (var damage in new[] { "truncated", "future-schema", "missing-settings-field", "missing-rules",
            "duplicate-provider", "missing-current-provider", "invalid-rule", "invalid-settings", "duplicate-property" })
        {
            var root = JsonNode.Parse(ConfigurationBackupCodec.Write(CreateSnapshot(true)))!.AsObject();
            switch (damage)
            {
                case "future-schema": root["schemaVersion"] = 2; break;
                case "missing-settings-field": root["settings"]!.AsObject().Remove("theme"); break;
                case "missing-rules": root.Remove("textHeaderMetadataRules"); break;
                case "duplicate-provider": root["providers"]!.AsArray().Add(root["providers"]![0]!.DeepClone()); break;
                case "missing-current-provider": root["settings"]!["currentProviderId"] = Guid.NewGuid().ToString(); break;
                case "invalid-rule": root["chapterRules"]![0]!["pattern"] = "["; break;
                case "invalid-settings": root["settings"]!["defaultSpeakSpeed"] = 500; break;
            }
            var json = damage switch
            {
                "truncated" => "{",
                "duplicate-property" => root.ToJsonString().Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1", StringComparison.Ordinal),
                _ => root.ToJsonString()
            };
            await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Backup.PrepareRestoreAsync(json, CancellationToken.None));
            Assert.Equal(before, await fixture.Backup.CreateBackupAsync(CancellationToken.None));
            Assert.False(notified);
        }
    }

    [Fact]
    public async Task Failure_after_partial_database_replacement_rolls_back_every_configuration_table()
    {
        using var fixture = await Fixture.CreateAsync();
        await fixture.RestoreAsync(CreateSnapshot(false));
        var before = await fixture.Backup.CreateBackupAsync(CancellationToken.None);
        var next = CreateSnapshot(true);
        await fixture.ExecuteAsync($"""
            CREATE TRIGGER RejectRestore BEFORE INSERT ON TextHeaderMetadataRules
            WHEN NEW.Id = '{next.TextHeaderMetadataRules[0].Id}' BEGIN SELECT RAISE(ABORT, 'test failure'); END;
            """);
        var notified = false;
        fixture.CurrentProvider.Changed += (_, _) => notified = true;
        await Assert.ThrowsAsync<SqliteException>(() => fixture.RestoreAsync(next));
        Assert.Equal(before, await fixture.Backup.CreateBackupAsync(CancellationToken.None));
        Assert.False(notified);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Settings_failure_or_cancellation_after_file_replacement_compensates_old_snapshot(bool cancel)
    {
        using var fixture = await Fixture.CreateAsync();
        await fixture.RestoreAsync(CreateSnapshot(false));
        var before = await fixture.Backup.CreateBackupAsync(CancellationToken.None);
        fixture.SettingsFile.FailAfterNextSave = !cancel;
        using var cancellation = new CancellationTokenSource();
        if (cancel) fixture.SettingsFile.AfterNextSave = cancellation.Cancel;
        var plan = await fixture.Backup.PrepareRestoreAsync(ConfigurationBackupCodec.Write(CreateSnapshot(true)), CancellationToken.None);
        var notified = false;
        fixture.Settings.Changed += (_, _) => notified = true;
        await Assert.ThrowsAnyAsync<Exception>(() => fixture.Backup.RestoreAsync(plan, cancellation.Token));
        Assert.Equal(before, await fixture.Backup.CreateBackupAsync(CancellationToken.None));
        Assert.Equal(ConfigurationBackupCodec.Read(before).Settings.CurrentProviderId,
            (await fixture.SettingsFile.LoadAsync(CancellationToken.None)).CurrentProviderId);
        Assert.False(notified);
    }

    [Fact]
    public async Task Concurrent_settings_update_is_serialized_after_restore_and_observer_failure_does_not_hide_success()
    {
        using var fixture = await Fixture.CreateAsync();
        var saving = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.SettingsFile.BeforeNextSave = async () => { saving.SetResult(); await release.Task; };
        fixture.Settings.Changed += (_, _) => throw new InvalidOperationException("observer failure");
        var notified = false;
        fixture.Settings.Changed += (_, _) => notified = true;
        var next = CreateSnapshot(true);
        var restore = fixture.RestoreAsync(next);
        await saving.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var update = fixture.Settings.UpdateAsync(new AppSettingsUpdate { PlaybackVolume = 0.75 }, CancellationToken.None);
        Assert.False(update.IsCompleted);
        release.SetResult();
        await restore;
        // Existing ordinary updates propagate observer failures, but their persistence still succeeds.
        await Assert.ThrowsAsync<InvalidOperationException>(() => update);
        Assert.True(notified);
        Assert.Equal(next.Settings.CurrentProviderId, fixture.Settings.Current.CurrentProviderId);
        Assert.Equal(0.75, fixture.Settings.Current.PlaybackVolume);
        Assert.Equal(0.75, (await fixture.SettingsFile.LoadAsync(CancellationToken.None)).PlaybackVolume);
    }

    private static ConfigurationSnapshot CreateSnapshot(bool configuredEdge)
    {
        var now = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var edge = new SpeechProviderInstance(ProviderId.New(), "Microsoft Edge", 3,
            new EdgeSpeechProviderConfiguration(configuredEdge ? new EdgeVoice("voice", "Friendly", "zh-CN", "Female") : null), now, now);
        var http = new SpeechProviderInstance(ProviderId.New(), "Private HTTP", 17,
            new HttpSpeechProviderConfiguration("https://example.invalid/tts?key=private-api-key", "POST",
                new Dictionary<string, string> { ["Authorization"] = "Bearer private-token", ["Cookie"] = "private-cookie" },
                "private-body-secret {{text}}", new ProviderRequestRateLimit(5, 1000)), now, now);
        var settings = (AppSettings.Default with
        {
            Theme = "Dark",
            LogLevel = "Debug",
            CurrentProviderId = configuredEdge ? edge.Id : http.Id,
            EnableLongParagraphSplitting = false,
            LongParagraphThreshold = 500,
            SplitChaptersOnBlankLines = true,
            DefaultSpeakSpeed = 73,
            PrefetchCount = 1,
            ReadChapterTitle = false,
            PlaybackVolume = 0.4,
            CacheLimitBytes = AppSettings.MinCacheLimitBytes,
            MainWindowCloseBehavior = MainWindowCloseBehavior.ExitApplication,
            StartMinimizedToTray = true,
            MiniPlayerLeft = 101,
            MiniPlayerTop = 202,
            MiniPlayerTopmost = true,
            EnablePerformanceTelemetry = true,
            EnabledExperimentalFeatureIds = configuredEdge ? [ExperimentalFeaturesService.MicrosoftEdgeTts, "unknown"] : ["unknown"]
        }).Normalize();
        return new ConfigurationSnapshot(settings, [edge, http],
            [new ChapterRule(Guid.NewGuid().ToString(), "Chapter", "^chapter", 5, false, now, now)],
            [new RegexReplacementRule(Guid.NewGuid(), "Replace", true, 10, "word", "replacement", RegexReplacementScope.Both, now, now)],
            [new FileNameMetadataRule(Guid.NewGuid().ToString(), "Filename", "(?<title>.+)", 15, false, now, now)],
            [new TextHeaderMetadataRule(Guid.NewGuid().ToString(), "Header", "(?<description>.+)", 20, true, now, now)]);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly TemporaryDirectory _temporary = new("NovelSpeakerConfigurationBackupTests");
        public AppDataDirectoryProvider Directories { get; }
        public SqliteConnectionFactory Connections { get; }
        public FaultSettingsStore SettingsFile { get; }
        public AppSettingsService Settings { get; }
        public CurrentSpeechProvider CurrentProvider { get; }
        public RegexReplacementRuleWorkspaceService RegexRules { get; }
        public ConfigurationBackupService Backup { get; }

        private Fixture()
        {
            Directories = new AppDataDirectoryProvider(_temporary.Path);
            Connections = new SqliteConnectionFactory(Directories, null, pooling: false);
            SettingsFile = new FaultSettingsStore(new JsonAppSettingsStore(Directories));
            Settings = new AppSettingsService(SettingsFile, AppSettings.Default);
            var providerStore = new SqliteProviderStore(Connections);
            var providers = new SpeechProviderWorkspace(providerStore, TimeProvider.System, Settings);
            CurrentProvider = new CurrentSpeechProvider(providerStore, new ProviderRuntimeResolver(providerStore, null, null), Settings, providers);
            RegexRules = new RegexReplacementRuleWorkspaceService(new RegexReplacementRuleRepository(Connections, TimeProvider.System),
                new RegexReplacementRuleErrorStore(), TimeProvider.System);
            Backup = new ConfigurationBackupService(new ConfigurationSnapshotStore(Connections, SettingsFile), Settings, providers, RegexRules);
        }

        public static async Task<Fixture> CreateAsync()
        {
            var fixture = new Fixture();
            await fixture.Directories.EnsureCreatedAsync(CancellationToken.None);
            await new SqliteMigrationRunner(fixture.Connections).InitializeAsync(CancellationToken.None);
            return fixture;
        }

        public async Task RestoreAsync(ConfigurationSnapshot snapshot) =>
            await Backup.RestoreAsync(await Backup.PrepareRestoreAsync(ConfigurationBackupCodec.Write(snapshot), CancellationToken.None), CancellationToken.None);

        public async Task ExecuteAsync(string sql)
        {
            await using var connection = await Connections.OpenConnectionAsync(CancellationToken.None);
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
        }

        public async Task<string> ReadOtherRowsAsync()
        {
            string[] tables = ["Books", "BookSources", "LocalBookSources", "Chapters", "LocalChapterContents", "ReadingProgress", "ChapterSpeechPlans", "ChapterSpeechPlanSegments", "SynthesisProfiles", "AudioCacheEntries", "AppMetadata", "SchemaVersion"];
            var results = new Dictionary<string, List<object[]>>();
            await using var connection = await Connections.OpenConnectionAsync(CancellationToken.None);
            foreach (var table in tables)
            {
                await using var command = connection.CreateCommand();
                command.CommandText = $"SELECT * FROM {table} ORDER BY 1;";
                await using var reader = await command.ExecuteReaderAsync();
                results[table] = [];
                while (await reader.ReadAsync())
                {
                    var values = new object[reader.FieldCount];
                    reader.GetValues(values);
                    results[table].Add(values);
                }
            }
            return JsonSerializer.Serialize(results);
        }

        public void Dispose() { CurrentProvider.Dispose(); Settings.Dispose(); _temporary.Dispose(); }
    }

    private sealed class FaultSettingsStore(IAppSettingsStore inner) : IAppSettingsStore
    {
        public bool FailAfterNextSave { get; set; }
        public Action? AfterNextSave { get; set; }
        public Func<Task>? BeforeNextSave { get; set; }
        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken) => inner.LoadAsync(cancellationToken);
        public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
        {
            if (BeforeNextSave is { } before) { BeforeNextSave = null; await before(); }
            await inner.SaveAsync(settings, cancellationToken);
            if (AfterNextSave is { } after) { AfterNextSave = null; after(); cancellationToken.ThrowIfCancellationRequested(); }
            if (FailAfterNextSave) { FailAfterNextSave = false; throw new IOException("test save failure"); }
        }
    }
}
