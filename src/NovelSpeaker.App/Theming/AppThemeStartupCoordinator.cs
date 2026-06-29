using NovelSpeaker.Application.Settings;
using NovelSpeaker.Domain.Settings;

namespace NovelSpeaker.App.Theming;

public sealed class AppThemeStartupCoordinator
{
    private readonly IAppSettingsStore _settingsStore;
    private readonly IThemeRuntime _themeRuntime;

    public AppThemeStartupCoordinator(IAppSettingsStore settingsStore, IThemeRuntime themeRuntime)
    {
        _settingsStore = settingsStore;
        _themeRuntime = themeRuntime;
    }

    public async Task ApplyAsync(CancellationToken cancellationToken)
    {
        var settings = await _settingsStore.LoadAsync(cancellationToken);
        Apply(settings.Theme);
    }

    public void Apply(string? theme)
    {
        switch (Normalize(theme))
        {
            case AppTheme.Light:
                _themeRuntime.ApplyLightTheme();
                break;
            case AppTheme.Dark:
                _themeRuntime.ApplyDarkTheme();
                break;
            default:
                _themeRuntime.ApplySystemTheme();
                break;
        }
    }

    internal static AppTheme Normalize(string? theme)
    {
        return string.IsNullOrWhiteSpace(theme)
            ? AppTheme.System
            : theme.Trim().ToLowerInvariant() switch
            {
                "light" => AppTheme.Light,
                "dark" => AppTheme.Dark,
                "system" => AppTheme.System,
                _ => AppSettings.DefaultTheme.Equals(theme, StringComparison.OrdinalIgnoreCase)
                    ? AppTheme.System
                    : AppTheme.System
            };
    }
}
