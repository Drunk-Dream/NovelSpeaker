using NovelSpeaker.Application.Settings;
using NovelSpeaker.Domain.Settings;

namespace NovelSpeaker.App.Shared.Theming;

public sealed class AppThemeStartupCoordinator
{
    private readonly IAppSettingsService _settingsService;
    private readonly IThemeRuntime _themeRuntime;

    public AppThemeStartupCoordinator(IAppSettingsService settingsService, IThemeRuntime themeRuntime)
    {
        _settingsService = settingsService;
        _themeRuntime = themeRuntime;
    }

    public Task ApplyAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Apply(_settingsService.Current.Theme);
        return Task.CompletedTask;
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
