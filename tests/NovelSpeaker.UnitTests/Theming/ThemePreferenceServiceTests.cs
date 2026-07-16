using NovelSpeaker.Application.Settings;
using NovelSpeaker.App.Theming;
using NovelSpeaker.Domain.Settings;
using Xunit;

namespace NovelSpeaker.UnitTests.Theming;

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

        var result = await service.ApplyAsync("Dark", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("System", result.EffectiveTheme);
        Assert.Equal(1, runtime.DarkCalls);
        Assert.Equal(1, runtime.SystemCalls);
    }

    [Fact]
    public async Task ApplyAsync_keeps_latest_request_as_final_persisted_theme()
    {
        var firstSaveGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondSaveGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new SequencedAppSettingsStore(AppSettings.Default, firstSaveGate.Task, secondSaveGate.Task);
        var runtime = new FakeThemeRuntime();
        var service = new ThemePreferenceService(store, new AppThemeStartupCoordinator(store, runtime));

        var firstTask = service.ApplyAsync("Light", CancellationToken.None);
        var secondTask = service.ApplyAsync("Dark", CancellationToken.None);

        firstSaveGate.SetResult();
        secondSaveGate.SetResult();

        await Task.WhenAll(firstTask, secondTask);

        Assert.Equal("Dark", store.Settings.Theme);
        Assert.Equal(1, runtime.LightCalls);
        Assert.Equal(1, runtime.DarkCalls);
    }

    private class FakeAppSettingsStore : IAppSettingsStore, IAppSettingsService
    {
        public FakeAppSettingsStore(AppSettings settings)
        {
            Settings = settings;
        }

        public AppSettings Settings { get; protected set; }

        public AppSettings Current => Settings;
        public event EventHandler<AppSettingsChangedEventArgs>? Changed { add { } remove { } }

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
            return SaveAndReturnAsync(Settings with
            {
                Theme = update.Theme ?? Settings.Theme
            }, cancellationToken);
        }

        private async Task<AppSettings> SaveAndReturnAsync(AppSettings settings, CancellationToken cancellationToken)
        {
            await SaveAsync(settings, cancellationToken);
            return Settings;
        }
    }

    private sealed class SequencedAppSettingsStore : FakeAppSettingsStore
    {
        private readonly Queue<Task> _saveGates;

        public SequencedAppSettingsStore(AppSettings settings, params Task[] saveGates)
            : base(settings)
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

    private sealed class FakeThemeRuntime : IThemeRuntime
    {
        public int SystemCalls { get; private set; }

        public int LightCalls { get; private set; }

        public int DarkCalls { get; private set; }

        public void ApplySystemTheme() => SystemCalls++;

        public void ApplyLightTheme() => LightCalls++;

        public void ApplyDarkTheme() => DarkCalls++;
    }
}
