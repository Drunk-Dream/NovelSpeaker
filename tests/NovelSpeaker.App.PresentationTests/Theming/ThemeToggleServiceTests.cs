using NovelSpeaker.Application.Settings;
using NovelSpeaker.App.Shared.Theming;
using NovelSpeaker.Domain.Settings;
using Xunit;

namespace NovelSpeaker.App.PresentationTests.Theming;

public sealed class ThemeToggleServiceTests
{
    [Theory]
    [InlineData(AppTheme.Light, "Dark")]
    [InlineData(AppTheme.Dark, "Light")]
    public async Task ToggleLightDarkAsync_uses_effective_theme_and_persists_an_explicit_target(
        AppTheme effectiveTheme,
        string expectedTheme)
    {
        var settings = new FakeAppSettingsService(AppSettings.Default with { Theme = "System" });
        var runtime = new FakeThemeRuntime(effectiveTheme);
        var service = new ThemePreferenceService(
            settings,
            new AppThemeStartupCoordinator(settings, runtime));
        var changedCount = 0;
        service.EffectiveThemeChanged += (_, _) => changedCount++;

        var result = await service.ToggleLightDarkAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.IsStale);
        Assert.Equal(expectedTheme, settings.Current.Theme);
        Assert.Equal(expectedTheme, result.EffectiveTheme);
        Assert.Equal(1, changedCount);
        Assert.NotEqual("System", settings.Current.Theme);
    }

    [Fact]
    public async Task Consecutive_toggles_are_serialized_against_the_latest_effective_theme()
    {
        var settings = new FakeAppSettingsService(AppSettings.Default with { Theme = "System" });
        var runtime = new FakeThemeRuntime(AppTheme.Light);
        var service = new ThemePreferenceService(
            settings,
            new AppThemeStartupCoordinator(settings, runtime));

        var first = service.ToggleLightDarkAsync(CancellationToken.None);
        var second = service.ToggleLightDarkAsync(CancellationToken.None);
        await Task.WhenAll(first, second);

        Assert.Equal("Light", settings.Current.Theme);
        Assert.Equal(AppTheme.Light, service.EffectiveTheme);
        Assert.Equal(1, runtime.LightApplyCount);
        Assert.Equal(1, runtime.DarkApplyCount);
    }

    private sealed class FakeAppSettingsService : IAppSettingsService
    {
        public FakeAppSettingsService(AppSettings settings)
        {
            Current = settings;
        }

        public AppSettings Current { get; private set; }

        public event EventHandler<AppSettingsChangedEventArgs>? Changed;

        public Task<AppSettings> UpdateAsync(AppSettingsUpdate update, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var previous = Current;
            Current = Current with { Theme = update.Theme ?? Current.Theme };
            Changed?.Invoke(this, new AppSettingsChangedEventArgs(previous, Current));
            return Task.FromResult(Current);
        }
    }

    private sealed class FakeThemeRuntime : IThemeRuntime
    {
        public FakeThemeRuntime(AppTheme effectiveTheme)
        {
            EffectiveTheme = effectiveTheme;
        }

        public AppTheme EffectiveTheme { get; private set; }

        public int LightApplyCount { get; private set; }

        public int DarkApplyCount { get; private set; }

        public void ApplySystemTheme()
        {
        }

        public void ApplyLightTheme()
        {
            LightApplyCount++;
            EffectiveTheme = AppTheme.Light;
        }

        public void ApplyDarkTheme()
        {
            DarkApplyCount++;
            EffectiveTheme = AppTheme.Dark;
        }
    }
}
