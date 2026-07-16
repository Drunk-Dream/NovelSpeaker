using NovelSpeaker.Application.Settings;
using NovelSpeaker.Domain.Settings;
using NovelSpeaker.Infrastructure.Settings;
using Xunit;

namespace NovelSpeaker.UnitTests.Settings;

public sealed class AppSettingsServiceTests
{
    [Fact]
    public async Task UpdateAsync_normalizes_values_from_partial_update()
    {
        var store = new FakeAppSettingsStore(AppSettings.Default);
        var service = new AppSettingsService(store);

        var settings = await service.UpdateAsync(
            new AppSettingsUpdate
            {
                DefaultSpeakSpeed = 99,
                PrefetchCount = 8,
                BookFileNameTemplate = "  {{name}}  "
            },
            CancellationToken.None);

        Assert.Equal(AppSettings.MaxSpeakSpeed, settings.DefaultSpeakSpeed);
        Assert.Equal(AppSettings.DefaultPrefetchCountValue, settings.PrefetchCount);
        Assert.Equal("{{name}}", settings.BookFileNameTemplate);
    }

    [Fact]
    public async Task UpdateAsync_normalizes_cache_limit_to_minimum()
    {
        var store = new FakeAppSettingsStore(AppSettings.Default);
        var service = new AppSettingsService(store);

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
    public async Task UpdateAsync_keeps_latest_value_for_same_field()
    {
        var firstSaveGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondSaveGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new SequencedAppSettingsStore(AppSettings.Default, firstSaveGate.Task, secondSaveGate.Task);
        var service = new AppSettingsService(store);

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
        var service = new AppSettingsService(store);

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
        var service = new AppSettingsService(store);

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
        var service = new AppSettingsService(store);
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

        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(CurrentSettings);
        }

        public virtual Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
        {
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
