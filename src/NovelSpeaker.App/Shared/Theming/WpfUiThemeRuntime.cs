using System.Windows;
using Wpf.Ui.Appearance;

namespace NovelSpeaker.App.Shared.Theming;

public sealed class WpfUiThemeRuntime : IThemeRuntime
{
    public AppTheme EffectiveTheme => ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark
        ? AppTheme.Dark
        : AppTheme.Light;

    public void ApplySystemTheme()
    {
        InvokeOnUiThread(() =>
        {
            ApplicationThemeManager.ApplySystemTheme();
            NovelSpeakerPaletteRuntime.ApplySystemTheme();
        });
    }

    public void ApplyLightTheme()
    {
        InvokeOnUiThread(() => ApplyTheme(ApplicationTheme.Light));
    }

    public void ApplyDarkTheme()
    {
        InvokeOnUiThread(() => ApplyTheme(ApplicationTheme.Dark));
    }

    private static void ApplyTheme(ApplicationTheme theme)
    {
        ApplicationThemeManager.Apply(theme);
        NovelSpeakerPaletteRuntime.Apply(theme);
    }

    private static void InvokeOnUiThread(Action action)
    {
        var dispatcher = global::System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(action);
            return;
        }

        action();
    }
}
