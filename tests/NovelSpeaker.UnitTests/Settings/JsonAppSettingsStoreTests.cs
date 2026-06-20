using NovelSpeaker.Domain.Books;
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
}
