using System.Windows;
using Wpf.Ui.Appearance;

namespace NovelSpeaker.App.Shared.Theming;

public sealed class WpfUiThemeRuntime : IThemeRuntime
{
    private readonly ThemePaletteRuntime _paletteRuntime;

    public WpfUiThemeRuntime()
        : this(new ThemePaletteRuntime())
    {
    }

    internal WpfUiThemeRuntime(ThemePaletteRuntime paletteRuntime)
    {
        _paletteRuntime = paletteRuntime ?? throw new ArgumentNullException(nameof(paletteRuntime));
    }

    public void ApplySystemTheme()
    {
        InvokeOnUiThread(() =>
        {
            ApplicationThemeManager.ApplySystemTheme();
            ApplyPaletteAndCorrectProviderTheme(GetCurrentPaletteKind());
        });
    }

    public void ApplyLightTheme()
    {
        InvokeOnUiThread(() =>
        {
            ApplicationThemeManager.Apply(ApplicationTheme.Light);
            ApplyPaletteAndCorrectProviderTheme(ThemePaletteKind.Light);
        });
    }

    public void ApplyDarkTheme()
    {
        InvokeOnUiThread(() =>
        {
            ApplicationThemeManager.Apply(ApplicationTheme.Dark);
            ApplyPaletteAndCorrectProviderTheme(ThemePaletteKind.Dark);
        });
    }

    private void ApplyPaletteAndCorrectProviderTheme(ThemePaletteKind requestedPalette)
    {
        var result = _paletteRuntime.Apply(requestedPalette);
        if (result.EffectivePalette == ThemePaletteKind.Light && requestedPalette == ThemePaletteKind.Dark)
        {
            ApplicationThemeManager.Apply(ApplicationTheme.Light);
        }
    }

    private static ThemePaletteKind GetCurrentPaletteKind() =>
        ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark
            ? ThemePaletteKind.Dark
            : ThemePaletteKind.Light;

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
