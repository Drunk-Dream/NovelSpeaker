using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using System.Windows;
using System.Windows.Media;
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
    public void Borderless_button_styles_keep_theme_backed_interaction_states()
    {
        var content = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src",
            "NovelSpeaker.App",
            "Shared",
            "Theming",
            "Resources",
            "SemanticStyles.xaml"));

        Assert.Contains("x:Key=\"BorderlessIconButtonStyle\"", content);
        Assert.Contains("x:Key=\"BorderlessListItemButtonStyle\"", content);
        Assert.Contains("Property=\"IsMouseOver\"", content);
        Assert.Contains("Property=\"IsPressed\"", content);
        Assert.Contains("Property=\"IsKeyboardFocused\"", content);
        Assert.Contains("Property=\"IsEnabled\" Value=\"False\"", content);
        Assert.Contains("AccentBrush", content);
        Assert.Contains("x:Key=\"IconButtonControlTemplate\"", content);
        Assert.Contains("x:Key=\"MediaIconButtonControlTemplate\"", content);
        Assert.Contains("TargetName=\"KeyboardFocusRing\"", content);
        Assert.Contains("CornerRadius=\"{StaticResource IconButtonCornerRadius}\"", content);
        Assert.Contains("CornerRadius=\"{StaticResource MediaControlCornerRadius}\"", content);
        Assert.DoesNotContain(
            "<Setter Property=\"BorderThickness\" Value=\"1\" />",
            GetStyleElement(content, "BorderlessIconButtonStyle").ToString());
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
        Assert.Contains("Property=\"BorderThickness\" Value=\"0\"", lastRowStyle.ToString());
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
        Assert.Contains("Property=\"BorderThickness\" Value=\"0,0,0,2\"", selectedCardStyle.ToString());
        Assert.Equal(
            "{StaticResource SelectedCardContainerStyle}",
            (string?)selectableListItemStyle.Attribute("BasedOn"));
        Assert.Equal(
            "{StaticResource SelectableListItemContainerStyle}",
            (string?)selectableCardListItemStyle.Attribute("BasedOn"));
        Assert.Contains("SubtleBorderBrush", selectableCardListItemStyle.ToString());
        Assert.Contains("Property=\"BorderThickness\" Value=\"1\"", selectableCardListItemStyle.ToString());
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
        Assert.Contains("PrimaryPlaybackIconButtonStyle", playerView);
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
