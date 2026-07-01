using NovelSpeaker.Domain.Books;
using NovelSpeaker.Domain.Settings;
using NovelSpeaker.Infrastructure.FileSystem;
using NovelSpeaker.Infrastructure.Settings;
using Xunit;

namespace NovelSpeaker.UnitTests.Settings;

public sealed class JsonAppSettingsStoreTests
{
    [Fact]
    public async Task LoadAsync_returns_defaults_when_settings_file_does_not_exist()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
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
    }

    [Fact]
    public async Task SaveAsync_persists_updated_segmentation_settings()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
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
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var directories = new LocalAppDataDirectoryProvider(root);
        await directories.EnsureCreatedAsync(CancellationToken.None);
        var store = new JsonAppSettingsStore(directories);

        await store.SaveAsync(AppSettings.Default with { SelectedTtsRuleId = 42 }, CancellationToken.None);
        var reloaded = await store.LoadAsync(CancellationToken.None);

        Assert.Equal(42, reloaded.SelectedTtsRuleId);
    }

    [Fact]
    public async Task SaveAsync_persists_custom_file_name_template()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
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
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
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
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
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
              "BookFileNameTemplate": null
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
    }

    [Fact]
    public async Task LoadAsync_caps_prefetch_count_to_supported_range()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
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
    public async Task GetCurrent_does_not_resume_on_the_callers_synchronization_context()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var directories = new LocalAppDataDirectoryProvider(root);
        await directories.EnsureCreatedAsync(CancellationToken.None);
        var store = new JsonAppSettingsStore(directories);
        await store.SaveAsync(AppSettings.Default with
        {
            EnableLongParagraphSplitting = false,
            LongParagraphThreshold = 42
        }, CancellationToken.None);

        var previousContext = SynchronizationContext.Current;
        var trackingContext = new TrackingSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(trackingContext);

        try
        {
            var options = store.GetCurrent();

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
}
