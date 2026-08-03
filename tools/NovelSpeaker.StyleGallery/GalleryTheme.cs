using Wpf.Ui.Appearance;
using Wpf.Ui.Markup;
using System.Windows.Media;

namespace NovelSpeaker.StyleGallery;

public enum GalleryTheme
{
    Light,
    Dark
}

public static class GalleryThemeExtensions
{
    public static GalleryThemeChoice Parse(string value) =>
        value.Equals("dark", StringComparison.OrdinalIgnoreCase)
            ? GalleryThemeChoice.Dark
            : value.Equals("light", StringComparison.OrdinalIgnoreCase)
                ? GalleryThemeChoice.Light
                : value.Equals("all", StringComparison.OrdinalIgnoreCase)
                    ? GalleryThemeChoice.All
                : throw new GalleryUsageException($"Theme must be 'light', 'dark' or 'all', but was '{value}'.");

    public static GalleryTheme ToGalleryTheme(this GalleryThemeChoice theme) =>
        theme == GalleryThemeChoice.Dark ? GalleryTheme.Dark : GalleryTheme.Light;

    public static ApplicationTheme ToWpfUiTheme(this GalleryTheme theme) =>
        theme == GalleryTheme.Dark ? ApplicationTheme.Dark : ApplicationTheme.Light;

    public static string FileName(this GalleryTheme theme) => theme.ToString().ToLowerInvariant();
}

public static class GalleryThemeRuntime
{
    public static void EnsureProviderResources()
    {
        var application = System.Windows.Application.Current
            ?? throw new InvalidOperationException("Style Gallery resources require a WPF Application.");
        var dictionaries = application.Resources.MergedDictionaries;

        if (!dictionaries.OfType<ThemesDictionary>().Any())
        {
            dictionaries.Insert(0, new ThemesDictionary { Theme = ApplicationTheme.Light });
        }

        if (!dictionaries.OfType<ControlsDictionary>().Any())
        {
            dictionaries.Add(new ControlsDictionary());
        }
    }

    public static void Apply(GalleryTheme theme)
    {
        EnsureProviderResources();
        ApplicationThemeManager.Apply(theme.ToWpfUiTheme());

        var application = System.Windows.Application.Current!;
        SetBrush(application, "GalleryCanvasBackgroundBrush", theme == GalleryTheme.Dark ? "#101218" : "#F4F5F9");
        SetBrush(application, "GallerySurfaceBrush", theme == GalleryTheme.Dark ? "#1B1F27" : "#FFFFFF");
        SetBrush(application, "GalleryMutedSurfaceBrush", theme == GalleryTheme.Dark ? "#232832" : "#F1F3F8");
        SetBrush(application, "GalleryPrimaryTextBrush", theme == GalleryTheme.Dark ? "#F2F4F8" : "#20242C");
        SetBrush(application, "GallerySecondaryTextBrush", theme == GalleryTheme.Dark ? "#AEB5C1" : "#626A77");
        SetBrush(application, "GalleryTertiaryTextBrush", theme == GalleryTheme.Dark ? "#7F8794" : "#8A919D");
        SetBrush(application, "GalleryBorderBrush", theme == GalleryTheme.Dark ? "#3A414D" : "#D9DDE6");
        SetBrush(application, "GalleryAccentBrush", theme == GalleryTheme.Dark ? "#7C8CFF" : "#5B6FD8");
        SetBrush(application, "GalleryOnAccentTextBrush", theme == GalleryTheme.Dark ? "#101218" : "#FFFFFF");
    }

    private static void SetBrush(System.Windows.Application application, string key, string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)!);
        brush.Freeze();
        application.Resources[key] = brush;
    }
}
