using NovelSpeaker.Application.Settings;
using NovelSpeaker.App.Shared.Theming;
using NovelSpeaker.Domain.Settings;
using Xunit;

namespace NovelSpeaker.App.PresentationTests.Theming;

public sealed class AppThemeStartupCoordinatorTests
{
    [Fact]
    public async Task ApplyAsync_maps_supported_theme_values()
    {
        foreach (var (theme, systemCalls, lightCalls, darkCalls) in new[]
                 {
                     ("System", 1, 0, 0),
                     ("Light", 0, 1, 0),
                     ("Dark", 0, 0, 1)
                 })
        {
            var runtime = new FakeThemeRuntime();
            var coordinator = new AppThemeStartupCoordinator(
                new FakeAppSettingsStore(AppSettings.Default with { Theme = theme }),
                runtime);

            await coordinator.ApplyAsync(CancellationToken.None);

            Assert.Equal(systemCalls, runtime.SystemCalls);
            Assert.Equal(lightCalls, runtime.LightCalls);
            Assert.Equal(darkCalls, runtime.DarkCalls);
        }
    }

    [Fact]
    public void Apply_invalid_theme_value_falls_back_to_system()
    {
        var runtime = new FakeThemeRuntime();
        var coordinator = new AppThemeStartupCoordinator(new FakeAppSettingsStore(AppSettings.Default), runtime);

        coordinator.Apply("Blue");

        Assert.Equal(1, runtime.SystemCalls);
        Assert.Equal(0, runtime.LightCalls);
        Assert.Equal(0, runtime.DarkCalls);
    }

    private sealed class FakeAppSettingsStore : IAppSettingsService
    {
        private readonly AppSettings _settings;

        public FakeAppSettingsStore(AppSettings settings)
        {
            _settings = settings;
        }

        public AppSettings Current => _settings;
        public event EventHandler<AppSettingsChangedEventArgs>? Changed { add { } remove { } }
        public Task<AppSettings> UpdateAsync(AppSettingsUpdate update, CancellationToken cancellationToken) =>
            Task.FromResult(_settings);
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
