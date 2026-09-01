using NovelSpeaker.Application.Settings;
using NovelSpeaker.App.Shared.Theming;
using NovelSpeaker.Domain.Settings;
using Xunit;

namespace NovelSpeaker.App.PresentationTests.Theming;

public sealed class ThemePreferenceServiceTests
{
    [Fact]
    public async Task ApplyAsync_persists_and_applies_theme()
    {
        var store = new FakeAppSettingsStore(AppSettings.Default);
        var runtime = new FakeThemeRuntime();
        var service = new ThemePreferenceService(store, new AppThemeStartupCoordinator(store, runtime));

        var result = await service.ApplyAsync("Dark", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Dark", store.Settings.Theme);
        Assert.Equal(1, runtime.DarkCalls);
    }

    [Fact]
    public async Task ApplyAsync_rolls_back_runtime_on_failure()
    {
        var store = new FakeAppSettingsStore(AppSettings.Default) { SaveException = new InvalidOperationException("save failed") };
        var runtime = new FakeThemeRuntime();
        var service = new ThemePreferenceService(store, new AppThemeStartupCoordinator(store, runtime));
        var changedCount = 0;
        service.EffectiveThemeChanged += (_, _) => changedCount++;

        var result = await service.ApplyAsync("Dark", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("System", result.EffectiveTheme);
        Assert.Equal(1, runtime.DarkCalls);
        Assert.Equal(1, runtime.SystemCalls);
        Assert.Equal(1, changedCount);
    }

    [Fact]
    public async Task ApplyAsync_notifies_after_current_cancellation_rolls_back_runtime()
    {
        var saveGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new SequencedAppSettingsStore(AppSettings.Default, saveGate.Task);
        var runtime = new FakeThemeRuntime();
        var service = new ThemePreferenceService(store, new AppThemeStartupCoordinator(store, runtime));
        var changedCount = 0;
        service.EffectiveThemeChanged += (_, _) => changedCount++;
        using var cancellationSource = new CancellationTokenSource();

        var applyTask = service.ApplyAsync("Dark", cancellationSource.Token);
        await store.SaveEntered;
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => applyTask);

        Assert.Equal(1, runtime.DarkCalls);
        Assert.Equal(1, runtime.SystemCalls);
        Assert.Equal(1, changedCount);
    }

    [Fact]
    public async Task Waiter_cancellation_notifies_after_older_request_finishes_stale()
    {
        var firstSaveGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new SequencedAppSettingsStore(AppSettings.Default, firstSaveGate.Task);
        var runtime = new FakeThemeRuntime();
        var service = new ThemePreferenceService(store, new AppThemeStartupCoordinator(store, runtime));
        var changedCount = 0;
        service.EffectiveThemeChanged += (_, _) => changedCount++;
        using var cancellationSource = new CancellationTokenSource();

        var firstTask = service.ApplyAsync("Light", CancellationToken.None);
        await store.SaveEntered;
        var secondTask = service.ApplyAsync("Dark", cancellationSource.Token);
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => secondTask);
        firstSaveGate.SetResult();
        var firstResult = await firstTask;

        Assert.True(firstResult.IsStale);
        Assert.Equal("Light", store.Settings.Theme);
        Assert.Equal(AppTheme.Light, service.EffectiveTheme);
        Assert.Equal(1, changedCount);
    }

    [Fact]
    public async Task ApplyAsync_keeps_latest_request_as_final_persisted_theme()
    {
        var firstSaveGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondSaveGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new SequencedAppSettingsStore(AppSettings.Default, firstSaveGate.Task, secondSaveGate.Task);
        var runtime = new FakeThemeRuntime();
        var service = new ThemePreferenceService(store, new AppThemeStartupCoordinator(store, runtime));
        var changedCount = 0;
        service.EffectiveThemeChanged += (_, _) => changedCount++;

        var firstTask = service.ApplyAsync("Light", CancellationToken.None);
        var secondTask = service.ApplyAsync("Dark", CancellationToken.None);

        firstSaveGate.SetResult();
        secondSaveGate.SetResult();

        await Task.WhenAll(firstTask, secondTask);

        Assert.Equal("Dark", store.Settings.Theme);
        Assert.Equal(1, runtime.LightCalls);
        Assert.Equal(1, runtime.DarkCalls);
        Assert.Equal(1, changedCount);
    }

    [Fact]
    public async Task External_theme_setting_change_updates_runtime_and_notifies_shell()
    {
        var store = new FakeAppSettingsStore(AppSettings.Default);
        var runtime = new FakeThemeRuntime();
        var service = new ThemePreferenceService(store, new AppThemeStartupCoordinator(store, runtime));
        var changedCount = 0;
        service.EffectiveThemeChanged += (_, _) => changedCount++;

        await store.UpdateAsync(new AppSettingsUpdate { Theme = "Dark" }, CancellationToken.None);

        Assert.Equal(1, runtime.DarkCalls);
        Assert.Equal(AppTheme.Dark, service.EffectiveTheme);
        Assert.Equal(1, changedCount);
    }

    private class FakeAppSettingsStore : IAppSettingsStore, IAppSettingsService
    {
        public FakeAppSettingsStore(AppSettings settings)
        {
            Settings = settings;
        }

        public AppSettings Settings { get; protected set; }

        public AppSettings Current => Settings;
        public event EventHandler<AppSettingsChangedEventArgs>? Changed;

        public Exception? SaveException { get; set; }

        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(Settings);

        public virtual Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
        {
            if (SaveException is not null)
            {
                throw SaveException;
            }

            Settings = settings.Normalize();
            return Task.CompletedTask;
        }

        public Task<AppSettings> UpdateAsync(AppSettingsUpdate update, CancellationToken cancellationToken)
        {
            return UpdateCoreAsync(update, cancellationToken);
        }

        private async Task<AppSettings> UpdateCoreAsync(
            AppSettingsUpdate update,
            CancellationToken cancellationToken)
        {
            var previous = Settings;
            var settings = previous with { Theme = update.Theme ?? previous.Theme };
            await SaveAsync(settings, cancellationToken);
            Changed?.Invoke(this, new AppSettingsChangedEventArgs(previous, Settings));
            return Settings;
        }
    }

    private sealed class SequencedAppSettingsStore : FakeAppSettingsStore
    {
        private readonly Queue<Task> _saveGates;
        private readonly TaskCompletionSource _saveEntered = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public SequencedAppSettingsStore(AppSettings settings, params Task[] saveGates)
            : base(settings)
        {
            _saveGates = new Queue<Task>(saveGates);
        }

        public Task SaveEntered => _saveEntered.Task;

        public override async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
        {
            _saveEntered.TrySetResult();
            if (_saveGates.TryDequeue(out var gate))
            {
                await gate.WaitAsync(cancellationToken);
            }

            await base.SaveAsync(settings, cancellationToken);
        }
    }

    private sealed class FakeThemeRuntime : IThemeRuntime
    {
        public AppTheme EffectiveTheme { get; private set; } = AppTheme.Light;

        public int SystemCalls { get; private set; }

        public int LightCalls { get; private set; }

        public int DarkCalls { get; private set; }

        public void ApplySystemTheme()
        {
            SystemCalls++;
            EffectiveTheme = AppTheme.Light;
        }

        public void ApplyLightTheme()
        {
            LightCalls++;
            EffectiveTheme = AppTheme.Light;
        }

        public void ApplyDarkTheme()
        {
            DarkCalls++;
            EffectiveTheme = AppTheme.Dark;
        }
    }
}
