using System.Globalization;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml.Linq;
using NovelSpeaker.App.Shared.Theming;
using NovelSpeaker.TestKit.Wpf;
using Wpf.Ui.Appearance;
using Xunit;

namespace NovelSpeaker.App.WpfTests.Ui;

[Collection("WpfDispatcher")]
public sealed class InteractionPaletteContractTests
{
    private static readonly string[] PaletteFiles =
    [
        "Palette.Light.xaml",
        "Palette.Dark.xaml",
        "Palette.HighContrast.xaml"
    ];

    private static readonly string[] InteractionKeys =
    [
        "App.Brush.Interaction.Surface.Hover",
        "App.Brush.Interaction.Surface.Pressed",
        "App.Brush.Interaction.Border.Hover",
        "App.Brush.Interaction.Border.Pressed",
        "App.Brush.Interaction.Foreground.Hover",
        "App.Brush.Interaction.Foreground.Pressed",
        "App.Brush.Interaction.Foreground.Selected",
        "App.Brush.Interaction.Foreground.Disabled",
        "App.Brush.Accent.Subtle.Hover"
    ];

    [Fact]
    public void All_palette_variants_expose_one_interaction_brush_contract()
    {
        var paletteDirectory = Path.Combine(
            GetRepositoryRoot(),
            "src",
            "NovelSpeaker.App",
            "Shared",
            "Theming",
            "Palettes");
        var xamlNamespace = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
        var resources = PaletteFiles
            .Select(fileName => XDocument.Load(Path.Combine(paletteDirectory, fileName)).Root!.Elements().ToArray())
            .ToArray();

        Assert.All(resources, palette =>
        {
            var keys = palette
                .Select(resource => (string?)resource.Attribute(xamlNamespace + "Key")
                    ?? throw new InvalidOperationException("Palette resource is missing x:Key."))
                .ToArray();
            Assert.Equal(SemanticPaletteRuntime.Keys, keys);
            Assert.All(palette, resource => Assert.Equal("SolidColorBrush", resource.Name.LocalName));
            Assert.All(InteractionKeys, key => Assert.Contains(
                palette,
                resource => (string?)resource.Attribute(xamlNamespace + "Key") == key));
        });

        var highContrast = resources[2].ToDictionary(
            resource => (string)resource.Attribute(xamlNamespace + "Key")!,
            resource => (string?)resource.Attribute("Color"),
            StringComparer.Ordinal);
        Assert.Equal(
            "{DynamicResource {x:Static SystemColors.HighlightColorKey}}",
            highContrast["App.Brush.Accent.Subtle.Hover"]);
        Assert.Equal(
            "{DynamicResource {x:Static SystemColors.HighlightColorKey}}",
            highContrast["App.Brush.Focus"]);
        Assert.Equal(
            "{DynamicResource {x:Static SystemColors.GrayTextColorKey}}",
            highContrast["App.Brush.Interaction.Foreground.Disabled"]);
        Assert.Equal(
            "{DynamicResource {x:Static SystemColors.HighlightTextColorKey}}",
            highContrast["App.Brush.Interaction.Foreground.Selected"]);
    }

    [Fact]
    public void Theme_switch_updates_open_dynamic_resource_consumers_without_replacing_styles()
    {
        WpfTestHost.RunInSta(() =>
        {
            var runtime = new WpfUiThemeRuntime();
            runtime.ApplyLightTheme();

            var root = new Grid();
            var consumer = new Border();
            consumer.SetResourceReference(
                Border.BackgroundProperty,
                "App.Brush.Interaction.Surface.Hover");
            root.Children.Add(consumer);
            using var host = WpfWindowHost.Show(new Window
            {
                Content = root,
                Width = 100,
                Height = 40,
                WindowStyle = WindowStyle.ToolWindow
            });
            host.Window.UpdateLayout();

            var application = Assert.IsAssignableFrom<global::System.Windows.Application>(
                global::System.Windows.Application.Current);
            var styles = application.FindResource("App.Button.Icon");
            var lightColor = Assert.IsType<SolidColorBrush>(consumer.Background).Color;

            runtime.ApplyDarkTheme();

            var darkColor = Assert.IsType<SolidColorBrush>(consumer.Background).Color;
            Assert.NotEqual(lightColor, darkColor);
            Assert.Same(styles, application.FindResource("App.Button.Icon"));

            runtime.ApplyLightTheme();
        });
    }

    [Fact]
    public void High_contrast_palette_projects_focus_selection_and_disabled_to_system_colors()
    {
        WpfTestHost.RunInSta(() =>
        {
            var application = Assert.IsAssignableFrom<global::System.Windows.Application>(
                global::System.Windows.Application.Current);
            var systemColors = new Dictionary<object, (bool Exists, object? Value)>
            {
                [SystemColors.WindowColorKey] = Capture(application, SystemColors.WindowColorKey),
                [SystemColors.WindowTextColorKey] = Capture(application, SystemColors.WindowTextColorKey),
                [SystemColors.ControlColorKey] = Capture(application, SystemColors.ControlColorKey),
                [SystemColors.HighlightColorKey] = Capture(application, SystemColors.HighlightColorKey),
                [SystemColors.HighlightTextColorKey] = Capture(application, SystemColors.HighlightTextColorKey),
                [SystemColors.GrayTextColorKey] = Capture(application, SystemColors.GrayTextColorKey)
            };

            foreach (var (key, value) in new[]
                     {
                         (SystemColors.WindowColorKey, Colors.Black),
                         (SystemColors.WindowTextColorKey, Colors.White),
                         (SystemColors.ControlColorKey, Colors.DarkGray),
                         (SystemColors.HighlightColorKey, Colors.Yellow),
                         (SystemColors.HighlightTextColorKey, Colors.Black),
                         (SystemColors.GrayTextColorKey, Colors.Gray)
                     })
            {
                application.Resources[key] = value;
            }

            try
            {
                SemanticPaletteRuntime.Apply(
                    application,
                    ApplicationTheme.Light,
                    "/NovelSpeaker;component/Shared/Theming/Palettes/Palette.Light.xaml",
                    "/NovelSpeaker;component/Shared/Theming/Palettes/Palette.Dark.xaml",
                    "/NovelSpeaker;component/Shared/Theming/Palettes/Palette.HighContrast.xaml",
                    useHighContrast: true);

                Assert.Equal(Colors.Yellow, ColorOf(application, "App.Brush.Focus"));
                Assert.Equal(
                    Colors.Yellow,
                    ColorOf(application, "App.Brush.Accent.Subtle.Hover"));
                Assert.NotEqual(
                    ColorOf(application, "App.Brush.Interaction.Foreground.Selected"),
                    ColorOf(application, "App.Brush.Interaction.Foreground.Disabled"));
                application.Resources[SystemColors.HighlightColorKey] = Colors.Magenta;
                application.Resources[SystemColors.HighlightTextColorKey] = Colors.White;
                SemanticPaletteRuntime.Apply(
                    application,
                    ApplicationTheme.Light,
                    "/NovelSpeaker;component/Shared/Theming/Palettes/Palette.Light.xaml",
                    "/NovelSpeaker;component/Shared/Theming/Palettes/Palette.Dark.xaml",
                    "/NovelSpeaker;component/Shared/Theming/Palettes/Palette.HighContrast.xaml",
                    useHighContrast: true);
                Assert.Equal(Colors.Magenta, ColorOf(application, "App.Brush.Focus"));
                Assert.NotEqual(
                    ColorOf(application, "App.Brush.Interaction.Foreground.Selected"),
                    ColorOf(application, "App.Brush.Interaction.Foreground.Disabled"));
                Assert.NotEqual(
                    ColorOf(application, "App.Brush.Focus"),
                    ColorOf(application, "App.Brush.Interaction.Foreground.Disabled"));
                Assert.Equal(
                    ColorOf(application, "App.Brush.Interaction.Foreground.Selected"),
                    ColorOf(application, "App.Brush.Accent.Text"));
            }
            finally
            {
                foreach (var (key, value) in systemColors)
                {
                    if (value.Exists)
                    {
                        application.Resources[key] = value.Value;
                    }
                    else
                    {
                        application.Resources.Remove(key);
                    }
                }

                new WpfUiThemeRuntime().ApplyLightTheme();
            }
        });
    }

    [Fact]
    public void System_parameter_change_entry_point_refreshes_dynamic_consumers()
    {
        WpfTestHost.RunInSta(() =>
        {
            var runtime = new WpfUiThemeRuntime();
            runtime.ApplyDarkTheme();
            var application = Assert.IsAssignableFrom<global::System.Windows.Application>(
                global::System.Windows.Application.Current);
            var consumer = new Border();
            consumer.SetResourceReference(
                Border.BackgroundProperty,
                "App.Brush.Interaction.Surface.Hover");
            using var host = WpfWindowHost.Show(new Window
            {
                Content = consumer,
                Width = 100,
                Height = 40,
                WindowStyle = WindowStyle.ToolWindow
            });
            host.Window.UpdateLayout();

            var expectedColor = ColorOf(application, "App.Brush.Interaction.Surface.Hover");
            var palette = application.Resources.MergedDictionaries.Single(dictionary =>
                dictionary.Source?.OriginalString.EndsWith(
                    "/Palette.Light.xaml",
                    StringComparison.OrdinalIgnoreCase) == true);
            palette["App.Brush.Interaction.Surface.Hover"] = new SolidColorBrush(Colors.Magenta);
            Assert.Equal(Colors.Magenta, Assert.IsType<SolidColorBrush>(consumer.Background).Color);

            NovelSpeakerPaletteRuntime.HandleSystemParametersChanged(
                new PropertyChangedEventArgs("HighContrast"));

            Assert.Equal(
                expectedColor,
                Assert.IsType<SolidColorBrush>(consumer.Background).Color);
            runtime.ApplyLightTheme();
        });
    }

    [Fact]
    public void System_parameter_callback_contract_guards_dispatcher_shutdown_and_threading()
    {
        var source = File.ReadAllText(Path.Combine(
            GetRepositoryRoot(),
            "src",
            "NovelSpeaker.App",
            "Shared",
            "Theming",
            "NovelSpeakerPaletteRuntime.cs"));
        Assert.Contains("Dispatcher.CheckAccess()", source, StringComparison.Ordinal);
        Assert.Contains("Dispatcher.BeginInvoke", source, StringComparison.Ordinal);
        Assert.Contains("HasShutdownStarted", source, StringComparison.Ordinal);
        Assert.Contains("HasShutdownFinished", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Motion_tokens_are_exact_and_animation_markup_uses_tokens()
    {
        var repositoryRoot = GetRepositoryRoot();
        var motionPath = Path.Combine(
            repositoryRoot,
            "src",
            "NovelSpeaker.App",
            "Shared",
            "Theming",
            "Resources",
            "Tokens",
            "Motion.xaml");
        var motionKeys = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
        var xamlNamespace = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
        var values = XDocument.Load(motionPath).Root!.Elements()
            .ToDictionary(
                resource => (string)resource.Attribute(motionKeys + "Key")!,
                resource => TimeSpan.Parse(resource.Value, CultureInfo.InvariantCulture),
                StringComparer.Ordinal);
        Assert.Equal(TimeSpan.FromMilliseconds(100), values["App.Motion.Fast"]);
        Assert.Equal(TimeSpan.FromMilliseconds(160), values["App.Motion.Standard"]);
        Assert.Equal(TimeSpan.FromMilliseconds(220), values["App.Motion.Slow"]);

        var xamlFiles = Directory.EnumerateFiles(
            Path.Combine(repositoryRoot, "src", "NovelSpeaker.App"),
            "*.xaml",
            SearchOption.AllDirectories);
        var literalDurations = xamlFiles
            .SelectMany(path => XDocument.Load(path).Descendants())
            .SelectMany(element => element.Attributes("Duration"))
            .Select(attribute => attribute.Value)
            .Where(value => !value.StartsWith("{StaticResource App.Motion.", StringComparison.Ordinal))
            .ToArray();
        Assert.Empty(literalDurations);

        var inputPath = Path.Combine(
            repositoryRoot,
            "src",
            "NovelSpeaker.App",
            "Shared",
            "Theming",
            "Resources",
            "Styles",
            "Inputs.xaml");
        var inputDocument = XDocument.Load(inputPath);
        var popup = inputDocument.Descendants()
            .Single(element => element.Name.LocalName == "Popup" &&
                               (string?)element.Attribute(xamlNamespace + "Name") == "PART_Popup");
        Assert.Equal("Fade", (string?)popup.Attribute("PopupAnimation"));
        var comboExitStoryboard = inputDocument.Descendants()
            .Single(element => element.Name.LocalName == "Trigger" &&
                               (string?)element.Attribute("Property") == "IsDropDownOpen" &&
                               element.Descendants().Any(animation =>
                                   animation.Name.LocalName == "DoubleAnimation" &&
                                   (string?)animation.Attribute("From") == "1" &&
                                   (string?)animation.Attribute("To") == "0"))
            .Descendants()
            .Where(element => element.Name.LocalName == "DoubleAnimation" &&
                              (string?)element.Attribute("Storyboard.TargetName") == "DropDownBorder")
            .ToArray();
        Assert.Contains(
            comboExitStoryboard,
            animation => (string?)animation.Attribute("Duration") == "{StaticResource App.Motion.Fast}" &&
                         (string?)animation.Attribute("From") == "1" &&
                         (string?)animation.Attribute("To") == "0");
    }

    private static (bool Exists, object? Value) Capture(
        global::System.Windows.Application application,
        object key) =>
        (application.Resources.Contains(key), application.Resources.Contains(key) ? application.Resources[key] : null);

    private static Color ColorOf(global::System.Windows.Application application, string key) =>
        Assert.IsType<SolidColorBrush>(application.FindResource(key)).Color;

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
