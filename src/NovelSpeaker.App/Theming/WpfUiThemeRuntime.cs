using Wpf.Ui.Appearance;

namespace NovelSpeaker.App.Theming;

public sealed class WpfUiThemeRuntime : IThemeRuntime
{
    public void ApplySystemTheme()
    {
        ApplicationThemeManager.ApplySystemTheme();
    }

    public void ApplyLightTheme()
    {
        ApplicationThemeManager.Apply(ApplicationTheme.Light);
    }

    public void ApplyDarkTheme()
    {
        ApplicationThemeManager.Apply(ApplicationTheme.Dark);
    }
}
