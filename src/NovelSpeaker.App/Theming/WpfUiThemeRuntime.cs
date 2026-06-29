using System.Windows;
using Wpf.Ui.Appearance;

namespace NovelSpeaker.App.Theming;

public sealed class WpfUiThemeRuntime : IThemeRuntime
{
    public void ApplySystemTheme()
    {
        InvokeOnUiThread(ApplicationThemeManager.ApplySystemTheme);
    }

    public void ApplyLightTheme()
    {
        InvokeOnUiThread(() => ApplicationThemeManager.Apply(ApplicationTheme.Light));
    }

    public void ApplyDarkTheme()
    {
        InvokeOnUiThread(() => ApplicationThemeManager.Apply(ApplicationTheme.Dark));
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
