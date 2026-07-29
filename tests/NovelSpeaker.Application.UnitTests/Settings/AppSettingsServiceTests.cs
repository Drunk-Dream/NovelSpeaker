using NovelSpeaker.Application.Settings;
using NovelSpeaker.Domain.Settings;
using Xunit;

namespace NovelSpeaker.Application.UnitTests.Settings;

public sealed class AppSettingsServiceTests
{
    [Fact]
    public void Current_uses_normalized_startup_snapshot_without_loading_store()
    {
        var store = new FakeAppSettingsStore(AppSettings.Default) { ThrowOnLoad = true };
        using var service = new AppSettingsService(
            store,
            AppSettings.Default with { DefaultSpeakSpeed = 99 });

        Assert.Equal(AppSettings.MaxSpeakSpeed, service.Current.DefaultSpeakSpeed);
        Assert.Equal(0, store.LoadCount);
        Assert.Equal(AppSettings.DefaultCacheLimitBytes, service.GetCurrentLimitBytes());
    }

    [Fact]
    public async Task UpdateAsync_publishes_ordered_previous_and_current_snapshots_after_save()
    {
        var store = new FakeAppSettingsStore(AppSettings.Default);
        using var service = new AppSettingsService(store, AppSettings.Default);
        var changes = new List<AppSettingsChangedEventArgs>();
        service.Changed += (_, change) => changes.Add(change);

        await service.UpdateAsync(new AppSettingsUpdate { DefaultSpeakSpeed = 11 }, CancellationToken.None);
        await service.UpdateAsync(new AppSettingsUpdate { PrefetchCount = 1 }, CancellationToken.None);

        Assert.Collection(
            changes,
            first =>
            {
                Assert.Equal(10, first.Previous.DefaultSpeakSpeed);
                Assert.Equal(11, first.Current.DefaultSpeakSpeed);
            },
            second =>
            {
                Assert.Equal(11, second.Previous.DefaultSpeakSpeed);
                Assert.Equal(1, second.Current.PrefetchCount);
            });
    }

    [Fact]
    public async Task UpdateAsync_cancelled_while_waiting_does_not_save_or_publish()
    {
        var saveStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new CancellableAppSettingsStore(AppSettings.Default, saveStarted);
        using var service = new AppSettingsService(store, AppSettings.Default);
        var changed = 0;
        service.Changed += (_, _) => changed++;
        var first = service.UpdateAsync(new AppSettingsUpdate { DefaultSpeakSpeed = 11 }, CancellationToken.None);
        await saveStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var cancellation = new CancellationTokenSource();
        var second = service.UpdateAsync(new AppSettingsUpdate { PrefetchCount = 1 }, cancellation.Token);

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
        store.AllowSave = true;
        store.ReleaseSave();
        await first;

        Assert.Equal(1, store.SaveCount);
        Assert.Equal(1, changed);
        Assert.Equal(2, service.Current.PrefetchCount);
    }

    [Fact]
    public async Task UpdateAsync_normalizes_values_from_partial_update()
    {
        var store = new FakeAppSettingsStore(AppSettings.Default);
        var service = new AppSettingsService(store, store.CurrentSettings);

        var settings = await service.UpdateAsync(
            new AppSettingsUpdate
            {
                DefaultSpeakSpeed = 99,
                PrefetchCount = 8,
                BookFileNameTemplate = "  {{name}}  ",
                PlaybackVolume = 2
            },
            CancellationToken.None);

        Assert.Equal(AppSettings.MaxSpeakSpeed, settings.DefaultSpeakSpeed);
        Assert.Equal(AppSettings.DefaultPrefetchCountValue, settings.PrefetchCount);
        Assert.Equal("{{name}}", settings.BookFileNameTemplate);
        Assert.Equal(AppSettings.DefaultPlaybackVolumeValue, settings.PlaybackVolume);
    }

    [Fact]
    public async Task UpdateAsync_normalizes_playback_volume_to_supported_range()
    {
        var store = new FakeAppSettingsStore(AppSettings.Default);
        using var service = new AppSettingsService(store, store.CurrentSettings);

        var muted = await service.UpdateAsync(
            new AppSettingsUpdate { PlaybackVolume = -0.25 },
            CancellationToken.None);
        var maximum = await service.UpdateAsync(
            new AppSettingsUpdate { PlaybackVolume = 1.25 },
            CancellationToken.None);

        Assert.Equal(0, muted.PlaybackVolume);
        Assert.Equal(1, maximum.PlaybackVolume);
        Assert.Equal(maximum, store.CurrentSettings);
    }

    [Fact]
    public async Task UpdateAsync_normalizes_cache_limit_to_minimum()
    {
        var store = new FakeAppSettingsStore(AppSettings.Default);
        var service = new AppSettingsService(store, store.CurrentSettings);

        var settings = await service.UpdateAsync(
            new AppSettingsUpdate
            {
                CacheLimitBytes = 16 * 1024 * 1024
            },
            CancellationToken.None);

        Assert.Equal(AppSettings.MinCacheLimitBytes, settings.CacheLimitBytes);
        Assert.Equal(AppSettings.MinCacheLimitBytes, service.GetCurrentLimitBytes());
    }

    [Fact]
    public async Task UpdateAsync_persists_desktop_lifecycle_preferences()
    {
        var store = new FakeAppSettingsStore(AppSettings.Default);
        var service = new AppSettingsService(store, store.CurrentSettings);

        var settings = await service.UpdateAsync(
            new AppSettingsUpdate
            {
                MainWindowCloseBehavior = MainWindowCloseBehavior.AskEveryTime,
                StartMinimizedToTray = true,
                MiniPlayerLeft = 120,
                MiniPlayerTop = 240,
                MiniPlayerTopmost = true
            },
            CancellationToken.None);

        Assert.Equal(MainWindowCloseBehavior.AskEveryTime, settings.MainWindowCloseBehavior);
        Assert.True(settings.StartMinimizedToTray);
        Assert.Equal(120, settings.MiniPlayerLeft);
        Assert.Equal(240, settings.MiniPlayerTop);
        Assert.True(settings.MiniPlayerTopmost);
        Assert.Equal(settings, store.CurrentSettings);
    }

    [Fact]
    public async Task UpdateAsync_keeps_latest_value_for_same_field()
    {
        var firstSaveGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondSaveGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new SequencedAppSettingsStore(AppSettings.Default, firstSaveGate.Task, secondSaveGate.Task);
        var service = new AppSettingsService(store, store.CurrentSettings);

        var firstTask = service.UpdateAsync(new AppSettingsUpdate { DefaultSpeakSpeed = 11 }, CancellationToken.None);
        var secondTask = service.UpdateAsync(new AppSettingsUpdate { DefaultSpeakSpeed = 12 }, CancellationToken.None);

        firstSaveGate.SetResult();
        secondSaveGate.SetResult();

        await Task.WhenAll(firstTask, secondTask);

        Assert.Equal(12, store.CurrentSettings.DefaultSpeakSpeed);
    }

    [Fact]
    public async Task UpdateAsync_merges_fields_from_serialized_concurrent_updates()
    {
        var firstSaveGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondSaveGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new SequencedAppSettingsStore(AppSettings.Default, firstSaveGate.Task, secondSaveGate.Task);
        var service = new AppSettingsService(store, store.CurrentSettings);

        var firstTask = service.UpdateAsync(new AppSettingsUpdate { DefaultSpeakSpeed = 15 }, CancellationToken.None);
        var secondTask = service.UpdateAsync(new AppSettingsUpdate { PrefetchCount = 1 }, CancellationToken.None);

        firstSaveGate.SetResult();
        secondSaveGate.SetResult();

        await Task.WhenAll(firstTask, secondTask);

        Assert.Equal(15, store.CurrentSettings.DefaultSpeakSpeed);
        Assert.Equal(1, store.CurrentSettings.PrefetchCount);
    }

    [Fact]
    public async Task UpdateAsync_preserves_last_successful_snapshot_after_failure()
    {
        var store = new FakeAppSettingsStore(AppSettings.Default)
        {
            SaveException = new InvalidOperationException("save failed")
        };
        var service = new AppSettingsService(store, store.CurrentSettings);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpdateAsync(
            new AppSettingsUpdate { DefaultSpeakSpeed = 16 },
            CancellationToken.None));

        store.SaveException = null;
        var settings = await service.UpdateAsync(
            new AppSettingsUpdate { PrefetchCount = 1 },
            CancellationToken.None);

        Assert.Equal(AppSettings.DefaultSpeakSpeedValue, settings.DefaultSpeakSpeed);
        Assert.Equal(1, settings.PrefetchCount);
    }

    [Fact]
    public async Task UpdateAsync_propagates_cancellation_without_replacing_the_last_successful_snapshot()
    {
        var saveStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new CancellableAppSettingsStore(AppSettings.Default, saveStarted);
        var service = new AppSettingsService(store, store.CurrentSettings);
        using var cancellation = new CancellationTokenSource();

        var updateTask = service.UpdateAsync(
            new AppSettingsUpdate { DefaultSpeakSpeed = 16 },
            cancellation.Token);
        await saveStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => updateTask);
        store.AllowSave = true;
        var settings = await service.UpdateAsync(
            new AppSettingsUpdate { PrefetchCount = 1 },
            CancellationToken.None);

        Assert.Equal(AppSettings.DefaultSpeakSpeedValue, settings.DefaultSpeakSpeed);
        Assert.Equal(1, settings.PrefetchCount);
    }

    private class FakeAppSettingsStore : IAppSettingsStore
    {
        public FakeAppSettingsStore(AppSettings currentSettings)
        {
            CurrentSettings = currentSettings.Normalize();
        }

        public AppSettings CurrentSettings { get; protected set; }

        public Exception? SaveException { get; set; }

        public bool ThrowOnLoad { get; set; }

        public int LoadCount { get; private set; }

        public int SaveCount { get; protected set; }

        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken)
        {
            LoadCount++;
            if (ThrowOnLoad)
            {
                throw new InvalidOperationException("unexpected load");
            }
            return Task.FromResult(CurrentSettings);
        }

        public virtual Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
        {
            SaveCount++;
            if (SaveException is not null)
            {
                throw SaveException;
            }

            CurrentSettings = settings.Normalize();
            return Task.CompletedTask;
        }
    }

    private sealed class SequencedAppSettingsStore : FakeAppSettingsStore
    {
        private readonly Queue<Task> _saveGates;

        public SequencedAppSettingsStore(AppSettings currentSettings, params Task[] saveGates)
            : base(currentSettings)
        {
            _saveGates = new Queue<Task>(saveGates);
        }

        public override async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
        {
            if (_saveGates.TryDequeue(out var gate))
            {
                await gate.WaitAsync(cancellationToken);
            }

            await base.SaveAsync(settings, cancellationToken);
        }
    }

    private sealed class CancellableAppSettingsStore : FakeAppSettingsStore
    {
        private readonly TaskCompletionSource _saveStarted;
        private readonly TaskCompletionSource _saveRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CancellableAppSettingsStore(AppSettings currentSettings, TaskCompletionSource saveStarted)
            : base(currentSettings)
        {
            _saveStarted = saveStarted;
        }

        public bool AllowSave { get; set; }

        public void ReleaseSave() => _saveRelease.TrySetResult();

        public override async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
        {
            if (!AllowSave)
            {
                _saveStarted.TrySetResult();
                await _saveRelease.Task.WaitAsync(cancellationToken);
            }

            await base.SaveAsync(settings, cancellationToken);
        }
    }
}
