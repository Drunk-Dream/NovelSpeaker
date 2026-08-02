using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using NovelSpeaker.App.Shared.Theming;
using NovelSpeaker.UnitTests;
using Wpf.Ui.Appearance;
using Xunit;

namespace NovelSpeaker.App.WpfTests.Ui;

[Collection("WpfDispatcher")]
public sealed class ThemeResourceTests
{
    private static readonly string[] ForbiddenWpfUiBrushKeys =
    {
        "ApplicationBackgroundBrush",
        "CardBackgroundFillColorDefaultBrush",
        "CardStrokeColorDefaultBrush",
        "ControlFillColorSecondaryBrush",
        "ControlFillColorTertiaryBrush",
        "LayerFillColorAltBrush",
        "SolidBackgroundFillColorBaseBrush",
        "TextFillColorPrimaryBrush",
        "TextFillColorSecondaryBrush",
        "AccentFillColorDefaultBrush",
        "AccentFillColorSecondaryBrush",
        "TextOnAccentFillColorPrimaryBrush",
        "SystemFillColorCriticalBrush",
        "SystemFillColorCriticalBackgroundBrush"
    };

    [Fact]
    public void App_xaml_files_do_not_contain_fixed_hex_colors()
    {
        var appRoot = Path.Combine(GetRepositoryRoot(), "src", "NovelSpeaker.App");

        foreach (var relativePath in new[]
                 {
                     Path.Combine("Shared", "Theming", "Resources", "SemanticStyles.xaml"),
                     Path.Combine("Bootstrap", "StartupStatusWindow.xaml"),
                     Path.Combine("Shell", "MainWindow.xaml"),
                     Path.Combine("Features", "Playback", "Components", "PlayerView.xaml")
                 })
        {
            var content = File.ReadAllText(Path.Combine(appRoot, relativePath));
            Assert.DoesNotMatch("#[0-9A-Fa-f]{3,8}", content);
        }
    }

    [Fact]
    public void Palette_files_have_the_same_semantic_brush_keys()
    {
        var paletteDirectory = Path.Combine(
            GetRepositoryRoot(),
            "src",
            "NovelSpeaker.App",
            "Shared",
            "Theming",
            "Resources",
            "Themes");

        var lightKeys = ReadResourceKeys(Path.Combine(paletteDirectory, "Palette.Light.xaml"));
        var darkKeys = ReadResourceKeys(Path.Combine(paletteDirectory, "Palette.Dark.xaml"));

        Assert.True(lightKeys.SetEquals(darkKeys));
        Assert.True(
            new[]
            {
                "AppBackgroundBrush", "CanvasSurfaceBrush", "PrimarySurfaceBrush", "SecondarySurfaceBrush",
                "RaisedSurfaceBrush", "PrimaryTextBrush", "SecondaryTextBrush", "TertiaryTextBrush",
                "SubtleBorderBrush", "StrongBorderBrush", "AccentBrush", "AccentForegroundBrush", "DangerBrush",
                "WarningBrush", "SuccessBrush", "AccentHoverBrush", "AccentPressedBrush", "AccentSubtleBrush",
                "AccentSubtleHoverBrush", "AccentFocusRingBrush"
            }.All(lightKeys.Contains));
    }

    [Fact]
    public void Loaded_palette_exposes_semantic_brushes()
    {
        WpfTestHost.RunInSta(() =>
        {
            foreach (var key in ThemePaletteResourceKeys.SemanticKeys)
            {
                Assert.IsType<SolidColorBrush>(global::System.Windows.Application.Current.TryFindResource(key));
            }
        });
    }

    [Fact]
    public void Switching_palette_refreshes_dynamic_resources_used_by_open_windows()
    {
        WpfTestHost.RunInSta(() =>
        {
            var runtime = new ThemePaletteRuntime(
                global::System.Windows.Application.Current.Resources,
                new PackThemePaletteLoader());
            runtime.Apply(ThemePaletteKind.Light);

            var mainWindow = new Window();
            mainWindow.SetResourceReference(Window.BackgroundProperty, "AppBackgroundBrush");
            var miniPlayerWindow = new Window();
            miniPlayerWindow.SetResourceReference(Window.BackgroundProperty, "AppBackgroundBrush");
            var lightColor = Assert.IsType<SolidColorBrush>(mainWindow.Background).Color;

            var result = runtime.Apply(ThemePaletteKind.Dark);

            Assert.True(result.IsApplied);
            Assert.Equal(ThemePaletteKind.Dark, result.EffectivePalette);
            Assert.NotEqual(lightColor, Assert.IsType<SolidColorBrush>(mainWindow.Background).Color);
            Assert.Equal(
                Assert.IsType<SolidColorBrush>(mainWindow.Background).Color,
                Assert.IsType<SolidColorBrush>(miniPlayerWindow.Background).Color);
            Assert.Equal(
                Assert.IsType<SolidColorBrush>(global::System.Windows.Application.Current.TryFindResource("AppBackgroundBrush")).Color,
                Assert.IsType<SolidColorBrush>(mainWindow.Background).Color);

            runtime.Apply(ThemePaletteKind.Light);
        });
    }

    [Fact]
    public void Wpf_ui_theme_runtime_switches_provider_and_application_palette_together()
    {
        WpfTestHost.RunInSta(() =>
        {
            var runtime = new WpfUiThemeRuntime();

            runtime.ApplyLightTheme();
            Assert.Equal(
                Color.FromRgb(0xF4, 0xF5, 0xF9),
                Assert.IsType<SolidColorBrush>(global::System.Windows.Application.Current.TryFindResource("AppBackgroundBrush")).Color);

            runtime.ApplyDarkTheme();
            Assert.Equal(
                Color.FromRgb(0x10, 0x12, 0x18),
                Assert.IsType<SolidColorBrush>(global::System.Windows.Application.Current.TryFindResource("AppBackgroundBrush")).Color);

            runtime.ApplySystemTheme();
            var expectedColor = ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark
                ? Color.FromRgb(0x10, 0x12, 0x18)
                : Color.FromRgb(0xF4, 0xF5, 0xF9);
            Assert.Equal(
                expectedColor,
                Assert.IsType<SolidColorBrush>(global::System.Windows.Application.Current.TryFindResource("AppBackgroundBrush")).Color);

            runtime.ApplyLightTheme();
        });
    }

    [Fact]
    public void Missing_requested_palette_falls_back_to_light_palette()
    {
        WpfTestHost.RunInSta(() =>
        {
            var runtime = new ThemePaletteRuntime(
                global::System.Windows.Application.Current.Resources,
                new FailingPaletteLoader(ThemePaletteKind.Dark));

            var result = runtime.Apply(ThemePaletteKind.Dark);

            Assert.True(result.IsApplied);
            Assert.Equal(ThemePaletteKind.Light, result.EffectivePalette);
            Assert.True(result.UsedFallback);
            Assert.Equal(
                Assert.IsType<SolidColorBrush>(
                    new PackThemePaletteLoader().Load(ThemePaletteKind.Light)["AppBackgroundBrush"]).Color,
                Assert.IsType<SolidColorBrush>(global::System.Windows.Application.Current.TryFindResource("AppBackgroundBrush")).Color);
        });
    }

    [Fact]
    public void Inconsistent_palette_keys_fall_back_to_light_palette()
    {
        WpfTestHost.RunInSta(() =>
        {
            var runtime = new ThemePaletteRuntime(
                global::System.Windows.Application.Current.Resources,
                new InconsistentPaletteLoader());

            var result = runtime.Apply(ThemePaletteKind.Dark);

            Assert.True(result.IsApplied);
            Assert.Equal(ThemePaletteKind.Light, result.EffectivePalette);
            Assert.True(result.UsedFallback);
        });
    }

    [Fact]
    public void Missing_all_palette_files_keeps_existing_valid_palette()
    {
        WpfTestHost.RunInSta(() =>
        {
            var runtime = new ThemePaletteRuntime(
                global::System.Windows.Application.Current.Resources,
                new FailingPaletteLoader(ThemePaletteKind.Light, ThemePaletteKind.Dark));

            var before = Assert.IsType<SolidColorBrush>(global::System.Windows.Application.Current.TryFindResource("AppBackgroundBrush")).Color;
            var result = runtime.Apply(ThemePaletteKind.Dark);

            Assert.True(result.IsApplied);
            Assert.Equal(ThemePaletteKind.Light, result.EffectivePalette);
            Assert.True(result.UsedFallback);
            Assert.Equal(
                before,
                Assert.IsType<SolidColorBrush>(global::System.Windows.Application.Current.TryFindResource("AppBackgroundBrush")).Color);
        });
    }

    [Fact]
    public void App_xaml_does_not_consume_wpf_ui_color_keys()
    {
        var xamlFiles = Directory.EnumerateFiles(
            Path.Combine(GetRepositoryRoot(), "src", "NovelSpeaker.App"),
            "*.xaml",
            SearchOption.AllDirectories);

        var violations = xamlFiles
            .SelectMany(path => ForbiddenWpfUiBrushKeys
                .Where(key => File.ReadAllText(path).Contains(key, StringComparison.Ordinal))
                .Select(key => $"{Path.GetRelativePath(GetRepositoryRoot(), path)}: {key}"))
            .ToArray();

        Assert.Empty(violations);
    }

    [Fact]
    public void Semantic_styles_bind_to_application_semantic_resources()
    {
        var semanticStylesPath = Path.Combine(
            GetRepositoryRoot(),
            "src",
            "NovelSpeaker.App",
            "Shared",
            "Theming",
            "Resources",
            "SemanticStyles.xaml");
        var content = File.ReadAllText(semanticStylesPath);

        Assert.Contains("PrimaryTextBrush", content);
        Assert.Contains("SecondaryTextBrush", content);
        Assert.Contains("PrimarySurfaceBrush", content);
        Assert.Contains("SubtleBorderBrush", content);
        Assert.Contains("DangerBrush", content);
        Assert.DoesNotContain("TextFillColor", content);
        Assert.DoesNotContain("CardBackgroundFillColor", content);
    }

    [Fact]
    public void Design_tokens_have_unique_keys_and_complete_visual_scale()
    {
        var tokenPath = Path.Combine(
            GetRepositoryRoot(),
            "src",
            "NovelSpeaker.App",
            "Shared",
            "Theming",
            "Resources",
            "DesignTokens.xaml");
        var document = XDocument.Load(tokenPath);
        var xamlNamespace = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
        var keys = document
            .Root!
            .Elements()
            .Select(element => (string?)element.Attribute(xamlNamespace + "Key"))
            .Where(static key => key is not null)
            .Select(static key => key!)
            .ToArray();

        Assert.Equal(keys.Length, keys.Distinct(StringComparer.Ordinal).Count());

        WpfTestHost.RunInSta(() =>
        {
            var resources = global::System.Windows.Application.Current.Resources;
            foreach (var (key, expected) in new[]
                     {
                         ("Spacing4", 4d), ("Spacing8", 8d), ("Spacing12", 12d),
                         ("Spacing16", 16d), ("Spacing20", 20d), ("Spacing24", 24d),
                         ("Spacing32", 32d), ("Spacing40", 40d), ("Spacing48", 48d),
                         ("CompactIconButtonSize", 32d), ("IconButtonSize", 36d),
                         ("InputControlHeight", 36d), ("TextControlHeight", 40d),
                         ("ListRowMinHeight", 48d), ("SettingsRowMinHeight", 52d),
                         ("MediaControlButtonSize", 44d), ("PrimaryMediaControlButtonSize", 48d),
                         ("ProgressTrackHeight", 4d), ("ProgressSliderHeight", 20d),
                         ("ProgressThumbSize", 18d), ("IconSize16", 16d), ("IconSize18", 18d),
                         ("IconSize20", 20d), ("IconSize24", 24d)
                     })
            {
                Assert.Equal(expected, Assert.IsType<double>(resources[key]));
            }

            Assert.Equal(new CornerRadius(16), Assert.IsType<CornerRadius>(resources["PageCornerRadius"]));
            Assert.Equal(new CornerRadius(14), Assert.IsType<CornerRadius>(resources["ContentCornerRadius"]));
            Assert.Equal(new CornerRadius(10), Assert.IsType<CornerRadius>(resources["CardCornerRadius"]));
            Assert.Equal(new CornerRadius(12), Assert.IsType<CornerRadius>(resources["DialogCornerRadius"]));
            Assert.Equal(new CornerRadius(10), Assert.IsType<CornerRadius>(resources["ListRowCornerRadius"]));
            Assert.Equal(new CornerRadius(8), Assert.IsType<CornerRadius>(resources["SmallControlCornerRadius"]));
            Assert.Equal(new CornerRadius(999), Assert.IsType<CornerRadius>(resources["MediaControlCornerRadius"]));
            Assert.Equal(new Thickness(1), Assert.IsType<Thickness>(resources["StandardBorderThickness"]));
            Assert.Equal(new Thickness(1), Assert.IsType<Thickness>(resources["KeyboardFocusRingThickness"]));
            foreach (var (key, expected) in new[]
                     {
                         ("SectionHeadingSpacing", new Thickness(0, 24, 0, 0)),
                         ("FieldControlSpacing", new Thickness(0, 8, 0, 0)),
                         ("FieldDescriptionSpacing", new Thickness(0, 8, 0, 0)),
                         ("ToolbarActionMargin", new Thickness(0, 0, 8, 8)),
                         ("ToolbarItemMargin", new Thickness(8, 0, 0, 0)),
                         ("ToolbarItemTrailingMargin", new Thickness(0, 0, 8, 0)),
                         ("ListHeaderMargin", new Thickness(16, 16, 16, 12)),
                         ("EmptyListMessageMargin", new Thickness(16, 0, 16, 16)),
                         ("ListViewportMargin", new Thickness(12, 0, 12, 12)),
                         ("ListItemHeaderMargin", new Thickness(12, 12, 12, 0)),
                         ("ListItemContentMargin", new Thickness(12, 12, 12, 12)),
                         ("ListItemSpacing", new Thickness(0, 0, 0, 8)),
                         ("FormSectionSpacing", new Thickness(0, 24, 0, 0)),
                         ("CompactActionPadding", new Thickness(12, 4, 12, 4)),
                         ("ToolbarActionPadding", new Thickness(12, 4, 12, 4)),
                         ("SecondaryActionPadding", new Thickness(16, 8, 16, 8)),
                         ("SmallActionPadding", new Thickness(12, 4, 12, 4))
                     })
            {
                Assert.Equal(expected, Assert.IsType<Thickness>(resources[key]));
            }

            var spacingScale = new HashSet<double> { 0d, 4d, 8d, 12d, 16d, 20d, 24d, 32d, 40d, 48d };
            foreach (var key in new[]
                     {
                         "PagePadding", "PageSectionSpacing", "SectionSpacing", "ContentSpacing", "FieldSpacing",
                         "ControlSpacing", "SectionHeadingSpacing", "FieldControlSpacing", "FieldDescriptionSpacing",
                         "TinySpacing", "ButtonGapMargin", "InlineGapMargin", "ToolbarActionMargin", "ToolbarItemMargin",
                         "ToolbarItemTrailingMargin", "ListHeaderMargin", "EmptyListMessageMargin", "ListViewportMargin",
                         "ListItemHeaderMargin", "ListItemContentMargin", "ListItemSpacing", "CardPadding", "CardPaddingLarge",
                         "CardContentPadding", "DialogPadding", "SettingsGroupPadding", "SettingsRowPadding",
                         "SettingsRowControlMargin", "ListRowPadding", "ListRowSpacing", "ButtonPadding", "CompactButtonPadding",
                         "CompactActionPadding", "ToolbarActionPadding", "SecondaryActionPadding", "SmallActionPadding",
                         "FormSectionSpacing", "IconToTextMargin", "TrailingIconMargin"
                     })
            {
                var thickness = Assert.IsType<Thickness>(resources[key]);
                Assert.All(
                    new[] { thickness.Left, thickness.Top, thickness.Right, thickness.Bottom },
                    component => Assert.Contains(component, spacingScale));
            }

            Assert.Equal(
                "Segoe UI Variable Text, Microsoft YaHei UI, Segoe UI, sans-serif",
                Assert.IsType<FontFamily>(resources["AppFontFamily"]).Source);

            Assert.Equal(TimeSpan.FromMilliseconds(100), Assert.IsType<Duration>(resources["AnimFast"]).TimeSpan);
            Assert.Equal(TimeSpan.FromMilliseconds(160), Assert.IsType<Duration>(resources["AnimNormal"]).TimeSpan);
            Assert.Equal(TimeSpan.FromMilliseconds(220), Assert.IsType<Duration>(resources["AnimSlow"]).TimeSpan);
            Assert.Equal(TimeSpan.Zero, Assert.IsType<Duration>(resources["AnimReducedMotion"]).TimeSpan);
            Assert.Equal(0d, Assert.IsType<double>(resources["ReducedMotionOffset"]));
            Assert.Equal(1d, Assert.IsType<double>(resources["ReducedMotionScale"]));
        });
    }

    [Fact]
    public void Missing_animation_duration_resource_keeps_the_220ms_fallback()
    {
        var missingResource = (object?)null;
        var configuredDuration = new Duration(TimeSpan.FromMilliseconds(160));

        Assert.Equal(
            TimeSpan.FromMilliseconds(220),
            global::NovelSpeaker.App.Features.Playback.Components.PlayerView.ResolveAnimationDuration(missingResource));
        Assert.Equal(
            TimeSpan.FromMilliseconds(220),
            global::NovelSpeaker.App.Features.BookDetails.BookDetailsPage.ResolveAnimationDuration(missingResource));
        Assert.Equal(
            configuredDuration.TimeSpan,
            global::NovelSpeaker.App.Features.Playback.Components.PlayerView.ResolveAnimationDuration(configuredDuration));
        Assert.Equal(
            configuredDuration.TimeSpan,
            global::NovelSpeaker.App.Features.BookDetails.BookDetailsPage.ResolveAnimationDuration(configuredDuration));
    }

    [Fact]
    public void Elevation_tokens_stay_within_the_visual_system_ranges()
    {
        WpfTestHost.RunInSta(() =>
        {
            var resources = global::System.Windows.Application.Current.Resources;
            var low = Assert.IsType<DropShadowEffect>(resources["ElevationLow"]);
            var medium = Assert.IsType<DropShadowEffect>(resources["ElevationMedium"]);
            var high = Assert.IsType<DropShadowEffect>(resources["ElevationHigh"]);

            Assert.InRange(low.ShadowDepth, 1d, 2d);
            Assert.InRange(low.BlurRadius, 8d, 12d);
            Assert.InRange(medium.ShadowDepth, 3d, 4d);
            Assert.InRange(medium.BlurRadius, 16d, 20d);
            Assert.InRange(high.ShadowDepth, 5d, 6d);
            Assert.InRange(high.BlurRadius, 22d, 28d);
            Assert.All(new[] { low, medium, high }, effect => Assert.InRange(effect.Opacity, 0d, 0.25d));
        });
    }

    [Fact]
    public void Shared_views_reference_tokens_for_repeated_public_dimensions()
    {
        var appRoot = Path.Combine(GetRepositoryRoot(), "src", "NovelSpeaker.App");
        var paths = new[]
        {
            Path.Combine("Shared", "Theming", "Resources", "SemanticStyles.xaml"),
            Path.Combine("Shell", "MainWindow.xaml"),
            Path.Combine("Desktop", "MiniPlayer", "MiniPlayerWindow.xaml"),
            Path.Combine("Features", "Library", "BookCardView.xaml"),
            Path.Combine("Features", "Library", "LibraryPage.xaml"),
            Path.Combine("Features", "Playback", "Components", "PlayerView.xaml"),
            Path.Combine("Features", "BookDetails", "BookDetailsPage.xaml"),
            Path.Combine("Features", "Cache", "CacheManagementPage.xaml"),
            Path.Combine("Features", "ChapterRules", "ChapterRulesPage.xaml"),
            Path.Combine("Features", "RegexReplacementRules", "RegexReplacementRulesPage.xaml"),
            Path.Combine("Features", "TtsRules", "TtsRulesPage.xaml")
        };

        var repeatedPublicLiterals = new[]
        {
            "Margin=\"0,18,0,0\"",
            "Margin=\"0,6,0,0\"",
            "Margin=\"0,10,0,0\"",
            "Padding=\"10,6\"",
            "Padding=\"12,6\"",
            "Padding=\"14,8\"",
            "Margin=\"0,22,0,0\"",
            "Margin=\"16,16,16,12\"",
            "Margin=\"16,0,16,16\"",
            "Margin=\"12,0,12,12\"",
            "Margin=\"14,12,14,0\"",
            "Margin=\"14,12,14,12\"",
            "Margin=\"0,0,0,10\"",
            "Padding=\"10,4\""
        };

        foreach (var relativePath in paths)
        {
            var content = File.ReadAllText(Path.Combine(appRoot, relativePath));
            Assert.DoesNotContain("CornerRadius=\"999\"", content);
            Assert.DoesNotContain("Height=\"4\"", content);
            Assert.DoesNotContain("Padding=\"16,8\"", content);
            Assert.DoesNotContain("Padding=\"14,12\"", content);
            Assert.DoesNotContain("BorderThickness=\"1\"", content);
            foreach (var literal in repeatedPublicLiterals)
            {
                Assert.DoesNotContain(literal, content);
            }
        }

        var libraryPage = File.ReadAllText(Path.Combine(appRoot, "Features", "Library", "LibraryPage.xaml"));
        Assert.Contains("ItemHeight=\"{StaticResource TextControlHeight}\"", libraryPage);
        Assert.DoesNotContain("ItemHeight=\"{StaticResource ListRowMinHeight}\"", libraryPage);

        var styles = File.ReadAllText(Path.Combine(
            appRoot,
            "Shared",
            "Theming",
            "Resources",
            "SemanticStyles.xaml"));
        Assert.Contains("StandardBorderThickness", styles);
        Assert.Contains("SelectedIndicatorThickness", styles);
        Assert.Contains("ListRowSpacing", styles);
        Assert.Contains("ProgressTrackHeight", styles);
        Assert.Contains("WindowTitleTextBlockStyle", styles);
        Assert.Contains("CardTitleTextBlockStyle", styles);
        Assert.Contains("BodyTextBlockStyle", styles);
        Assert.Contains("CaptionTextBlockStyle", styles);
    }

    [Fact]
    public void Borderless_button_styles_keep_theme_backed_interaction_states()
    {
        var buttons = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src",
            "NovelSpeaker.App",
            "Shared",
            "Theming",
            "Resources",
            "Components",
            "Buttons.xaml"));
        var mediaControls = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src",
            "NovelSpeaker.App",
            "Shared",
            "Theming",
            "Resources",
            "Components",
            "MediaControls.xaml"));

        Assert.Contains("x:Key=\"PrimaryButtonStyle\"", buttons);
        Assert.Contains("x:Key=\"SecondaryButtonStyle\"", buttons);
        Assert.Contains("x:Key=\"SubtleButtonStyle\"", buttons);
        Assert.Contains("x:Key=\"DangerButtonStyle\"", buttons);
        Assert.Contains("x:Key=\"IconButtonStyle\"", buttons);
        Assert.Contains("x:Key=\"BorderlessIconButtonStyle\"", buttons);
        Assert.Contains("x:Key=\"BorderlessListItemButtonStyle\"", buttons);
        Assert.Contains("Property=\"IsMouseOver\"", buttons);
        Assert.Contains("Property=\"IsPressed\"", buttons);
        Assert.Contains("Property=\"Selector.IsSelected\"", buttons);
        Assert.Contains("Property=\"IsKeyboardFocused\"", buttons);
        Assert.Contains("Property=\"IsEnabled\" Value=\"False\"", buttons);
        Assert.Contains("AccentBrush", buttons);
        Assert.Contains("x:Key=\"IconButtonControlTemplate\"", buttons);
        Assert.Contains("TargetName=\"KeyboardFocusRing\"", buttons);
        Assert.Contains("CornerRadius=\"{StaticResource IconButtonCornerRadius}\"", buttons);
        Assert.Contains("x:Key=\"MediaIconButtonControlTemplate\"", mediaControls);
        Assert.Contains("x:Key=\"PreviousChapterMediaButtonStyle\"", mediaControls);
        Assert.Contains("x:Key=\"PreviousSegmentMediaButtonStyle\"", mediaControls);
        Assert.Contains("x:Key=\"PlaybackMediaButtonStyle\"", mediaControls);
        Assert.Contains("x:Key=\"NextSegmentMediaButtonStyle\"", mediaControls);
        Assert.Contains("x:Key=\"NextChapterMediaButtonStyle\"", mediaControls);
        Assert.Contains("TargetName=\"KeyboardFocusRing\"", mediaControls);
        Assert.Contains("CornerRadius=\"{StaticResource MediaControlCornerRadius}\"", mediaControls);
        Assert.DoesNotContain("x:Key=\"IconButtonControlTemplate\"", File.ReadAllText(Path.Combine(
            GetRepositoryRoot(), "src", "NovelSpeaker.App", "Shared", "Theming", "Resources", "SemanticStyles.xaml")));
        Assert.DoesNotContain(
            "<Setter Property=\"BorderThickness\" Value=\"1\" />",
            GetStyleElement(buttons, "BorderlessIconButtonStyle").ToString());
    }

    [Fact]
    public void Window_and_mini_player_resources_have_one_chrome_owner()
    {
        var appRoot = Path.Combine(GetRepositoryRoot(), "src", "NovelSpeaker.App");
        var buttons = File.ReadAllText(Path.Combine(
            appRoot, "Shared", "Theming", "Resources", "Components", "Buttons.xaml"));
        var miniPlayer = File.ReadAllText(Path.Combine(
            appRoot, "Shared", "Theming", "Resources", "Windows", "MiniPlayer.xaml"));
        var mainWindow = File.ReadAllText(Path.Combine(appRoot, "Shell", "MainWindow.xaml"));

        Assert.Contains("x:Key=\"WindowChromeTitleBarStyle\"", buttons);
        Assert.Contains("x:Key=\"WindowChromeButtonStyle\"", buttons);
        Assert.Contains("x:Key=\"WindowOperationButtonStyle\"", buttons);
        Assert.Contains("x:Key=\"WindowCloseButtonStyle\"", buttons);
        Assert.Contains("Tag\" Value=\"WindowClose\"", buttons);
        Assert.Contains("DangerSubtleBrush", buttons);
        Assert.Contains("x:Key=\"MiniPlayerSurfaceStyle\"", miniPlayer);
        Assert.Contains("PageCornerRadius", miniPlayer);
        Assert.Contains("ElevationHigh", miniPlayer);
        Assert.Contains("WindowChromeTitleBarStyle", mainWindow);
    }

    [Fact]
    public void Settings_styles_share_row_tokens_and_borderless_interaction_template()
    {
        var appRoot = Path.Combine(GetRepositoryRoot(), "src", "NovelSpeaker.App");
        var tokens = File.ReadAllText(Path.Combine(
            appRoot,
            "Shared",
            "Theming",
            "Resources",
            "DesignTokens.xaml"));
        var styles = File.ReadAllText(Path.Combine(
            appRoot,
            "Shared",
            "Theming",
            "Resources",
            "SemanticStyles.xaml"));

        Assert.Contains("x:Key=\"SettingsRowMinHeight\"", tokens);
        Assert.Contains("x:Key=\"SettingsRowPadding\"", tokens);
        Assert.Contains("x:Key=\"SettingsGroupPadding\"", tokens);
        Assert.Contains("x:Key=\"SettingsRowControlMargin\"", tokens);
        Assert.Contains("x:Key=\"SettingsRowControlWidth\"", tokens);

        var rowsGroupStyle = GetStyleElement(styles, "SettingsRowsGroupBorderStyle");
        var settingsRowStyle = GetStyleElement(styles, "SettingsRowBorderStyle");
        var lastRowStyle = GetStyleElement(styles, "SettingsLastRowBorderStyle");
        var rowTitleStyle = GetStyleElement(styles, "SettingsRowTitleTextBlockStyle");
        var rowValueStyle = GetStyleElement(styles, "SettingsRowValueTextBlockStyle");
        var navigationRowStyle = GetStyleElement(styles, "SettingsNavigationRowButtonStyle");

        Assert.Contains("x:Key=\"SettingsNavigationRowContentTemplate\"", styles);
        Assert.Contains("CardCornerRadius", rowsGroupStyle.ToString());
        Assert.Contains("SettingsRowMinHeight", settingsRowStyle.ToString());
        Assert.Contains("SettingsRowPadding", settingsRowStyle.ToString());
        Assert.Contains("Property=\"VerticalAlignment\" Value=\"Center\"", rowTitleStyle.ToString());
        Assert.Contains("Property=\"VerticalAlignment\" Value=\"Center\"", rowValueStyle.ToString());
        Assert.Equal(
            "{StaticResource SettingsRowBorderStyle}",
            (string?)lastRowStyle.Attribute("BasedOn"));
        Assert.Contains("NoBorderThickness", lastRowStyle.ToString());
        Assert.Equal(
            "{StaticResource BorderlessListItemButtonStyle}",
            (string?)navigationRowStyle.Attribute("BasedOn"));
        Assert.Contains("SettingsRowMinHeight", navigationRowStyle.ToString());
        Assert.Contains("SettingsRowPadding", navigationRowStyle.ToString());
        Assert.Contains("SettingsNavigationRowContentTemplate", navigationRowStyle.ToString());
    }

    [Fact]
    public void Selected_cards_use_one_theme_backed_full_card_visual_state()
    {
        var styles = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src",
            "NovelSpeaker.App",
            "Shared",
            "Theming",
            "Resources",
            "SemanticStyles.xaml"));

        var selectedCardStyle = GetStyleElement(styles, "SelectedCardContainerStyle");
        var selectableListItemStyle = GetStyleElement(styles, "SelectableListItemContainerStyle");
        var selectableCardListItemStyle = GetStyleElement(styles, "SelectableCardListItemContainerStyle");

        Assert.Equal(
            "{StaticResource CardBorderStyle}",
            (string?)selectedCardStyle.Attribute("BasedOn"));
        Assert.Contains("Binding=\"{Binding IsSelected}\"", selectedCardStyle.ToString());
        Assert.Contains("SecondarySurfaceBrush", selectedCardStyle.ToString());
        Assert.Contains("AccentBrush", selectedCardStyle.ToString());
        Assert.Contains("SelectedIndicatorThickness", selectedCardStyle.ToString());
        Assert.Equal(
            "{StaticResource SelectedCardContainerStyle}",
            (string?)selectableListItemStyle.Attribute("BasedOn"));
        Assert.Equal(
            "{StaticResource SelectableListItemContainerStyle}",
            (string?)selectableCardListItemStyle.Attribute("BasedOn"));
        Assert.Contains("SubtleBorderBrush", selectableCardListItemStyle.ToString());
        Assert.Contains("StandardBorderThickness", selectableCardListItemStyle.ToString());
    }

    [Fact]
    public void Icon_and_list_buttons_use_shared_semantic_styles()
    {
        var appRoot = Path.Combine(GetRepositoryRoot(), "src", "NovelSpeaker.App");
        var playerView = File.ReadAllText(Path.Combine(appRoot, "Features", "Playback", "Components", "PlayerView.xaml"));
        var chapterRulesPage = File.ReadAllText(Path.Combine(appRoot, "Features", "ChapterRules", "ChapterRulesPage.xaml"));
        var regexReplacementRulesPage = File.ReadAllText(Path.Combine(
            appRoot,
            "Features",
            "RegexReplacementRules",
            "RegexReplacementRulesPage.xaml"));
        var libraryPage = File.ReadAllText(Path.Combine(appRoot, "Features", "Library", "LibraryPage.xaml"));
        var bookCardView = File.ReadAllText(Path.Combine(appRoot, "Features", "Library", "BookCardView.xaml"));

        Assert.Contains("ToolbarValueButtonStyle", playerView);
        Assert.Contains("PlaybackMediaButtonStyle", playerView);
        Assert.Contains("MediaIconButtonStyle", playerView);
        Assert.Contains("FloatingIconButtonStyle", playerView);
        Assert.Contains("BorderlessIconButtonStyle", libraryPage);
        Assert.Contains("BorderlessListItemButtonStyle", bookCardView);
        Assert.Contains("ReOrder24", chapterRulesPage);
        Assert.Contains("MoreHorizontal24", chapterRulesPage);
        Assert.Contains("Header=\"上移\"", chapterRulesPage);
        Assert.Contains("Header=\"下移\"", chapterRulesPage);
        Assert.Contains("ReOrder24", regexReplacementRulesPage);
        Assert.Contains("MoreHorizontal24", regexReplacementRulesPage);
        Assert.Contains("Header=\"上移\"", regexReplacementRulesPage);
        Assert.Contains("Header=\"下移\"", regexReplacementRulesPage);
    }

    [Fact]
    public void Icon_buttons_expose_tooltips_and_automation_names()
    {
        var appRoot = Path.Combine(GetRepositoryRoot(), "src", "NovelSpeaker.App");
        var xamlFiles = Directory
            .EnumerateFiles(appRoot, "*.xaml", SearchOption.AllDirectories)
            .Where(static path => !path.Contains(
                $"{Path.DirectorySeparatorChar}Shared{Path.DirectorySeparatorChar}Theming{Path.DirectorySeparatorChar}Resources{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal));
        var violations = xamlFiles
            .SelectMany(FindIconButtonsWithoutAccessibleMetadata)
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"Found icon buttons without Tooltip and AutomationProperties.Name:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }

    [Fact]
    public void Cache_cleanup_buttons_use_short_action_except_explicit_clear_all_danger_action()
    {
        var appRoot = Path.Combine(GetRepositoryRoot(), "src", "NovelSpeaker.App");
        var cleanupLabels = Directory
            .EnumerateFiles(appRoot, "*.xaml", SearchOption.AllDirectories)
            .Select(XDocument.Load)
            .SelectMany(static document => document
                .Descendants()
                .Where(static element => element.Name.LocalName == "Button")
                .Select(static element => (string?)element.Attribute("Content")))
            .Where(static content => content?.Contains("清理", StringComparison.Ordinal) == true)
            .ToArray();

        Assert.All(
            cleanupLabels,
            static label => Assert.True(
                label is "清理" or "清理全部缓存",
                $"Unexpected cache cleanup button label: {label}"));
        Assert.Single(cleanupLabels, static label => label == "清理全部缓存");
    }

    [Fact]
    public void App_textblocks_explicitly_bind_to_semantic_text_styles()
    {
        var appRoot = Path.Combine(GetRepositoryRoot(), "src", "NovelSpeaker.App");
        var xamlFiles = Directory
            .EnumerateFiles(Path.Combine(appRoot, "Features"), "*.xaml", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(appRoot, "Shared"), "*.xaml", SearchOption.AllDirectories))
            .Concat(new[]
            {
                Path.Combine(appRoot, "Shell", "MainWindow.xaml"),
                Path.Combine(appRoot, "Bootstrap", "StartupStatusWindow.xaml")
            });

        var violations = xamlFiles
            .SelectMany(FindUnstyledTextBlocks)
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"Found TextBlock elements without explicit semantic style or foreground:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }

    private static IEnumerable<string> FindUnstyledTextBlocks(string xamlPath)
    {
        var document = XDocument.Load(xamlPath, LoadOptions.SetLineInfo);
        var xNamespace = document.Root?.GetDefaultNamespace() ?? XNamespace.None;

        foreach (var textBlock in document.Descendants(xNamespace + "TextBlock"))
        {
            if (textBlock.Attribute("Style") is not null ||
                textBlock.Attribute("Foreground") is not null ||
                textBlock.Elements().Any(static element => element.Name.LocalName == "TextBlock.Style"))
            {
                continue;
            }

            var lineInfo = (IXmlLineInfo)textBlock;
            yield return $"{Path.GetRelativePath(GetRepositoryRoot(), xamlPath)}:{lineInfo.LineNumber}";
        }
    }

    private static IEnumerable<string> FindIconButtonsWithoutAccessibleMetadata(string xamlPath)
    {
        var document = XDocument.Load(xamlPath, LoadOptions.SetLineInfo);
        var presentationNamespace = document.Root?.GetDefaultNamespace() ?? XNamespace.None;
        var xamlNamespace = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");

        foreach (var button in document.Descendants(presentationNamespace + "Button"))
        {
            var containsIcon = button
                .Descendants()
                .Any(static element => element.Name.LocalName == "SymbolIcon");
            if (!containsIcon)
            {
                continue;
            }

            var hasToolTip = button.Attribute("ToolTip") is not null ||
                             button.Elements().Any(static element => element.Name.LocalName == "Button.ToolTip");
            var hasAutomationName = button
                .Attributes()
                .Any(attribute => attribute.Name.LocalName == "AutomationProperties.Name" &&
                                  attribute.Name.Namespace != xamlNamespace);
            if (hasToolTip && hasAutomationName)
            {
                continue;
            }

            var lineInfo = (IXmlLineInfo)button;
            yield return $"{Path.GetRelativePath(GetRepositoryRoot(), xamlPath)}:{lineInfo.LineNumber}";
        }
    }

    private static XElement GetStyleElement(string content, string key)
    {
        var document = XDocument.Parse(content);
        var xamlNamespace = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");

        return Assert.Single(
            document.Descendants(),
            element => element.Name.LocalName == "Style" &&
                       (string?)element.Attribute(xamlNamespace + "Key") == key);
    }

    private static HashSet<string> ReadResourceKeys(string path)
    {
        var document = XDocument.Load(path);
        var xamlNamespace = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
        return document
            .Descendants()
            .Select(element => (string?)element.Attribute(xamlNamespace + "Key"))
            .Where(static key => key is not null)
            .Select(static key => key!)
            .ToHashSet(StringComparer.Ordinal);
    }

    private sealed class FailingPaletteLoader : IThemePaletteLoader
    {
        private readonly HashSet<ThemePaletteKind> _failedPalettes;
        private readonly PackThemePaletteLoader _inner = new();

        public FailingPaletteLoader(params ThemePaletteKind[] failedPalettes)
        {
            _failedPalettes = failedPalettes.ToHashSet();
        }

        public ResourceDictionary Load(ThemePaletteKind palette)
        {
            if (_failedPalettes.Contains(palette))
            {
                throw new InvalidOperationException("Test palette load failure.");
            }

            return _inner.Load(palette);
        }
    }

    private sealed class InconsistentPaletteLoader : IThemePaletteLoader
    {
        private readonly PackThemePaletteLoader _inner = new();

        public ResourceDictionary Load(ThemePaletteKind palette)
        {
            var dictionary = _inner.Load(palette);
            if (palette == ThemePaletteKind.Dark)
            {
                dictionary["UnexpectedBrush"] = new SolidColorBrush(Colors.Magenta);
            }

            return dictionary;
        }
    }

    private static string GetRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "src")) &&
                Directory.Exists(Path.Combine(current.FullName, "docs")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
