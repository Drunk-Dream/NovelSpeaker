using NovelSpeaker.Application.Settings;
using NovelSpeaker.Domain.Books;
using NovelSpeaker.Domain.Settings;
using NovelSpeaker.Infrastructure.FileSystem;
using NovelSpeaker.Infrastructure.Settings;
using NovelSpeaker.TestKit.Common;
using Xunit;

namespace NovelSpeaker.Infrastructure.IntegrationTests;

public sealed class JsonAppSettingsStoreTests
{
    [Fact]
    public async Task LoadAsync_returns_defaults_when_settings_file_does_not_exist()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var root = temporaryDirectory.Path;
        var directories = new LocalAppDataDirectoryProvider(root);
        var store = new JsonAppSettingsStore(directories);

        var settings = await store.LoadAsync(CancellationToken.None);

        Assert.True(settings.EnableLongParagraphSplitting);
        Assert.Equal(300, settings.LongParagraphThreshold);
        Assert.Equal(10, settings.DefaultSpeakSpeed);
        Assert.Equal(2, settings.PrefetchCount);
        Assert.Equal("Information", settings.LogLevel);
        Assert.Equal("System", settings.Theme);
        Assert.Equal(AppSettings.DefaultBookFileNameTemplate, settings.BookFileNameTemplate);
        Assert.Equal(AppSettings.DefaultCacheLimitBytes, settings.CacheLimitBytes);
    }

    [Fact]
    public async Task SaveAsync_persists_updated_segmentation_settings()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var root = temporaryDirectory.Path;
        var directories = new LocalAppDataDirectoryProvider(root);
        await directories.EnsureCreatedAsync(CancellationToken.None);
        var store = new JsonAppSettingsStore(directories);

        var settings = (await store.LoadAsync(CancellationToken.None)) with
        {
            EnableLongParagraphSplitting = false,
            LongParagraphThreshold = 42
        };

        await store.SaveAsync(settings, CancellationToken.None);
        var reloaded = await store.LoadAsync(CancellationToken.None);

        Assert.False(reloaded.EnableLongParagraphSplitting);
        Assert.Equal(50, reloaded.LongParagraphThreshold);
    }

    [Fact]
    public async Task SaveAsync_persists_selected_tts_rule_id()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var root = temporaryDirectory.Path;
        var directories = new LocalAppDataDirectoryProvider(root);
        await directories.EnsureCreatedAsync(CancellationToken.None);
        var store = new JsonAppSettingsStore(directories);

        await store.SaveAsync(AppSettings.Default with { SelectedTtsRuleId = 42 }, CancellationToken.None);
        var reloaded = await store.LoadAsync(CancellationToken.None);

        Assert.Equal(42, reloaded.SelectedTtsRuleId);
    }

    [Fact]
    public async Task SaveAsync_persists_cache_limit()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var root = temporaryDirectory.Path;
        var directories = new LocalAppDataDirectoryProvider(root);
        await directories.EnsureCreatedAsync(CancellationToken.None);
        var store = new JsonAppSettingsStore(directories);

        await store.SaveAsync(AppSettings.Default with { CacheLimitBytes = 512L * 1024 * 1024 }, CancellationToken.None);
        var reloaded = await store.LoadAsync(CancellationToken.None);

        Assert.Equal(512L * 1024 * 1024, reloaded.CacheLimitBytes);
    }

    [Fact]
    public async Task SaveAsync_persists_desktop_lifecycle_preferences()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var directories = new LocalAppDataDirectoryProvider(temporaryDirectory.Path);
        await directories.EnsureCreatedAsync(CancellationToken.None);
        var store = new JsonAppSettingsStore(directories);

        await store.SaveAsync(
            AppSettings.Default with
            {
                MainWindowCloseBehavior = MainWindowCloseBehavior.ExitApplication,
                StartMinimizedToTray = true
            },
            CancellationToken.None);
        var reloaded = await store.LoadAsync(CancellationToken.None);

        Assert.Equal(MainWindowCloseBehavior.ExitApplication, reloaded.MainWindowCloseBehavior);
        Assert.True(reloaded.StartMinimizedToTray);
    }

    [Fact]
    public async Task SaveAsync_persists_custom_file_name_template()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var root = temporaryDirectory.Path;
        var directories = new LocalAppDataDirectoryProvider(root);
        await directories.EnsureCreatedAsync(CancellationToken.None);
        var store = new JsonAppSettingsStore(directories);

        await store.SaveAsync(
            AppSettings.Default with { BookFileNameTemplate = "《{{name}}》 - {{author}}" },
            CancellationToken.None);
        var reloaded = await store.LoadAsync(CancellationToken.None);

        Assert.Equal("《{{name}}》 - {{author}}", reloaded.BookFileNameTemplate);
    }

    [Fact]
    public async Task SaveAsync_preserves_empty_file_name_template()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var root = temporaryDirectory.Path;
        var directories = new LocalAppDataDirectoryProvider(root);
        await directories.EnsureCreatedAsync(CancellationToken.None);
        var store = new JsonAppSettingsStore(directories);

        await store.SaveAsync(
            AppSettings.Default with { BookFileNameTemplate = "   " },
            CancellationToken.None);
        var reloaded = await store.LoadAsync(CancellationToken.None);

        Assert.Equal(string.Empty, reloaded.BookFileNameTemplate);
    }

    [Fact]
    public async Task LoadAsync_normalizes_invalid_new_setting_values()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var root = temporaryDirectory.Path;
        var directories = new LocalAppDataDirectoryProvider(root);
        await directories.EnsureCreatedAsync(CancellationToken.None);
        await File.WriteAllTextAsync(
            directories.SettingsPath,
            """
            {
              "EnableLongParagraphSplitting": true,
              "LongParagraphThreshold": 40,
              "DefaultSpeakSpeed": 99,
              "PrefetchCount": -5,
              "LogLevel": "Verbose",
              "Theme": "Blue",
              "BookFileNameTemplate": null,
              "CacheLimitBytes": 1024
            }
            """,
            CancellationToken.None);
        var store = new JsonAppSettingsStore(directories);

        var settings = await store.LoadAsync(CancellationToken.None);

        Assert.Equal(50, settings.LongParagraphThreshold);
        Assert.Equal(20, settings.DefaultSpeakSpeed);
        Assert.Equal(2, settings.PrefetchCount);
        Assert.Equal("Information", settings.LogLevel);
        Assert.Equal("System", settings.Theme);
        Assert.Equal(AppSettings.DefaultBookFileNameTemplate, settings.BookFileNameTemplate);
        Assert.Equal(AppSettings.MinCacheLimitBytes, settings.CacheLimitBytes);
    }

    [Fact]
    public async Task LoadAsync_caps_prefetch_count_to_supported_range()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var root = temporaryDirectory.Path;
        var directories = new LocalAppDataDirectoryProvider(root);
        await directories.EnsureCreatedAsync(CancellationToken.None);
        await File.WriteAllTextAsync(
            directories.SettingsPath,
            """
            {
              "EnableLongParagraphSplitting": true,
              "LongParagraphThreshold": 300,
              "DefaultSpeakSpeed": 10,
              "PrefetchCount": 9,
              "LogLevel": "Information",
              "Theme": "System"
            }
            """,
            CancellationToken.None);
        var store = new JsonAppSettingsStore(directories);

        var settings = await store.LoadAsync(CancellationToken.None);

        Assert.Equal(2, settings.PrefetchCount);
    }

    [Fact]
    public async Task LoadAsync_returns_defaults_when_settings_json_is_corrupt()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var root = temporaryDirectory.Path;
        var directories = new LocalAppDataDirectoryProvider(root);
        await directories.EnsureCreatedAsync(CancellationToken.None);
        await File.WriteAllTextAsync(directories.SettingsPath, "{ invalid json", CancellationToken.None);
        var store = new JsonAppSettingsStore(directories);

        var settings = await store.LoadAsync(CancellationToken.None);

        Assert.Equal(AppSettings.Default, settings);
        Assert.False(File.Exists(directories.SettingsPath));
        Assert.Single(Directory.GetFiles(root, "settings.json.*.corrupt"));
    }

    [Fact]
    public async Task LoadAsync_uses_unique_corrupt_backup_names_for_the_same_utc_timestamp()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var root = temporaryDirectory.Path;
        var directories = new LocalAppDataDirectoryProvider(root);
        await directories.EnsureCreatedAsync(CancellationToken.None);
        var time = new FixedTimeProvider(new DateTimeOffset(2026, 7, 16, 12, 0, 0, TimeSpan.Zero));
        var store = new JsonAppSettingsStore(directories, time);

        await File.WriteAllTextAsync(directories.SettingsPath, "{ invalid", CancellationToken.None);
        await store.LoadAsync(CancellationToken.None);
        await File.WriteAllTextAsync(directories.SettingsPath, "{ invalid again", CancellationToken.None);
        await store.LoadAsync(CancellationToken.None);

        Assert.Equal(2, Directory.GetFiles(root, "settings.json.*.corrupt").Length);
    }

    [Fact]
    public async Task SaveAsync_atomically_replaces_existing_file_and_leaves_no_temporary_file()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var root = temporaryDirectory.Path;
        var directories = new LocalAppDataDirectoryProvider(root);
        await directories.EnsureCreatedAsync(CancellationToken.None);
        await File.WriteAllTextAsync(directories.SettingsPath, "old", CancellationToken.None);
        var store = new JsonAppSettingsStore(directories);

        await store.SaveAsync(AppSettings.Default with { Theme = "Dark" }, CancellationToken.None);

        Assert.Equal("Dark", (await store.LoadAsync(CancellationToken.None)).Theme);
        Assert.Empty(Directory.GetFiles(root, "*.tmp"));
    }

    [Theory]
    [InlineData(SettingsFileFailure.Write)]
    [InlineData(SettingsFileFailure.Flush)]
    [InlineData(SettingsFileFailure.Replace)]
    public async Task SaveAsync_failure_preserves_existing_file_and_cleans_temporary_file(SettingsFileFailure failure)
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var root = temporaryDirectory.Path;
        var directories = new LocalAppDataDirectoryProvider(root);
        await directories.EnsureCreatedAsync(CancellationToken.None);
        const string original = "original-settings";
        await File.WriteAllTextAsync(directories.SettingsPath, original, CancellationToken.None);
        var files = new FailingSettingsFileOperations(failure);
        var store = new JsonAppSettingsStore(
            directories,
            TimeProvider.System,
            files);

        await Assert.ThrowsAsync<IOException>(() =>
            store.SaveAsync(AppSettings.Default with { Theme = "Dark" }, CancellationToken.None));

        Assert.Equal(original, await File.ReadAllTextAsync(directories.SettingsPath, CancellationToken.None));
        Assert.Empty(Directory.GetFiles(root, "*.tmp"));
        if (failure == SettingsFileFailure.Write)
        {
            Assert.True(files.WriteAttempted);
        }
    }

    [Fact]
    public async Task SaveAsync_cancelled_after_flush_does_not_move_and_cleans_temporary_file()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var root = temporaryDirectory.Path;
        var directories = new LocalAppDataDirectoryProvider(root);
        await directories.EnsureCreatedAsync(CancellationToken.None);
        const string original = "original-settings";
        await File.WriteAllTextAsync(directories.SettingsPath, original, CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        var files = new CancelAfterFlushSettingsFileOperations(cancellation);
        var store = new JsonAppSettingsStore(directories, TimeProvider.System, files);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.SaveAsync(AppSettings.Default with { Theme = "Dark" }, cancellation.Token));

        Assert.True(files.FlushCompleted);
        Assert.Equal(0, files.MoveCount);
        Assert.Equal(original, await File.ReadAllTextAsync(directories.SettingsPath, CancellationToken.None));
        Assert.Empty(Directory.GetFiles(root, "*.tmp"));
    }

    [Fact]
    public async Task SaveAsync_pre_cancelled_preserves_existing_file_and_creates_no_temporary_file()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var root = temporaryDirectory.Path;
        var directories = new LocalAppDataDirectoryProvider(root);
        await directories.EnsureCreatedAsync(CancellationToken.None);
        await File.WriteAllTextAsync(directories.SettingsPath, "original", CancellationToken.None);
        var store = new JsonAppSettingsStore(directories);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.SaveAsync(AppSettings.Default with { Theme = "Dark" }, cancellation.Token));

        Assert.Equal("original", await File.ReadAllTextAsync(directories.SettingsPath, CancellationToken.None));
        Assert.Empty(Directory.GetFiles(root, "*.tmp"));
    }

    [Fact]
    public async Task GetCurrent_does_not_resume_on_the_callers_synchronization_context()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var root = temporaryDirectory.Path;
        var directories = new LocalAppDataDirectoryProvider(root);
        await directories.EnsureCreatedAsync(CancellationToken.None);
        var store = new JsonAppSettingsStore(directories);
        await store.SaveAsync(AppSettings.Default with
        {
            EnableLongParagraphSplitting = false,
            LongParagraphThreshold = 42
        }, CancellationToken.None);
        var startupSnapshot = await store.LoadAsync(CancellationToken.None);
        using var service = new AppSettingsService(store, startupSnapshot);

        var previousContext = SynchronizationContext.Current;
        var trackingContext = new TrackingSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(trackingContext);

        try
        {
            var options = service.GetCurrent();

            Assert.False(options.EnableLongParagraphSplitting);
            Assert.Equal(50, options.LongParagraphThreshold);
            Assert.Equal(0, trackingContext.PostCount);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }

    private sealed class TrackingSynchronizationContext : SynchronizationContext
    {
        private int _postCount;

        public int PostCount => Volatile.Read(ref _postCount);

        public override void Post(SendOrPostCallback d, object? state)
        {
            Interlocked.Increment(ref _postCount);
            ThreadPool.QueueUserWorkItem(static callbackState =>
            {
                var (callback, callbackArgument) = ((SendOrPostCallback Callback, object? State))callbackState!;
                callback(callbackArgument);
            }, (d, state));
        }
    }

    public enum SettingsFileFailure
    {
        Write,
        Flush,
        Replace
    }

    private sealed class FailingSettingsFileOperations(SettingsFileFailure failure) : ISettingsFileOperations
    {
        private readonly ISettingsFileOperations _inner = PhysicalSettingsFileOperations.Instance;

        public bool WriteAttempted { get; private set; }

        public bool Exists(string path) => _inner.Exists(path);

        public Stream OpenRead(string path) => _inner.OpenRead(path);

        public Stream CreateForWrite(string path)
        {
            var stream = _inner.CreateForWrite(path);
            if (failure == SettingsFileFailure.Write)
            {
                return new ThrowingWriteStream(stream, () => WriteAttempted = true);
            }

            return stream;
        }

        public void FlushToDisk(Stream stream)
        {
            if (failure == SettingsFileFailure.Flush)
            {
                throw new IOException("flush failed");
            }

            _inner.FlushToDisk(stream);
        }

        public void Move(string sourcePath, string destinationPath, bool overwrite)
        {
            if (failure == SettingsFileFailure.Replace && sourcePath.EndsWith(".tmp", StringComparison.Ordinal))
            {
                throw new IOException("move failed");
            }

            _inner.Move(sourcePath, destinationPath, overwrite);
        }

        public void Delete(string path) => _inner.Delete(path);
    }

    private sealed class CancelAfterFlushSettingsFileOperations(CancellationTokenSource cancellation) : ISettingsFileOperations
    {
        private readonly ISettingsFileOperations _inner = PhysicalSettingsFileOperations.Instance;

        public bool FlushCompleted { get; private set; }

        public int MoveCount { get; private set; }

        public bool Exists(string path) => _inner.Exists(path);

        public Stream OpenRead(string path) => _inner.OpenRead(path);

        public Stream CreateForWrite(string path) => _inner.CreateForWrite(path);

        public void FlushToDisk(Stream stream)
        {
            _inner.FlushToDisk(stream);
            FlushCompleted = true;
            cancellation.Cancel();
        }

        public void Move(string sourcePath, string destinationPath, bool overwrite)
        {
            MoveCount++;
            _inner.Move(sourcePath, destinationPath, overwrite);
        }

        public void Delete(string path) => _inner.Delete(path);
    }

    private sealed class ThrowingWriteStream(Stream inner, Action onWrite) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }

        public override void Flush() => inner.Flush();

        public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);

        public override void SetLength(long value) => inner.SetLength(value);

        public override void Write(byte[] buffer, int offset, int count)
        {
            onWrite();
            throw new IOException("write failed");
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            onWrite();
            return ValueTask.FromException(new IOException("write failed"));
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            onWrite();
            return Task.FromException(new IOException("write failed"));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await inner.DisposeAsync();
            GC.SuppressFinalize(this);
        }
    }
}
