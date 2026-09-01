namespace NovelSpeaker.App.Shared.Theming;

public interface IThemeRuntime
{
    AppTheme EffectiveTheme { get; }

    void ApplySystemTheme();

    void ApplyLightTheme();

    void ApplyDarkTheme();
}
