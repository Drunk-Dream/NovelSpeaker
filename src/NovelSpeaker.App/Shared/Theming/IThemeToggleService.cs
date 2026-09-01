namespace NovelSpeaker.App.Shared.Theming;

/// <summary>
/// Exposes the shell-only Light/Dark shortcut without broadening the settings-page theme contract.
/// </summary>
public interface IThemeToggleService
{
    AppTheme EffectiveTheme { get; }

    event EventHandler? EffectiveThemeChanged;

    Task<ThemePreferenceChangeResult> ToggleLightDarkAsync(CancellationToken cancellationToken);
}
