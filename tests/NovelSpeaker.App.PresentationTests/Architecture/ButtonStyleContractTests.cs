using System.Globalization;
using System.Xml.Linq;
using Xunit;

namespace NovelSpeaker.App.PresentationTests.Architecture;

public sealed class ButtonStyleContractTests
{
    private static readonly ArchitectureTestRepository Repository = ArchitectureTestRepository.Locate();
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void Visible_button_styles_use_provider_state_properties_as_the_surface_owner()
    {
        var styles = ReadStyles("Buttons.xaml");
        foreach (var (key, style) in ReadStyles("Media.xaml"))
        {
            styles.Add(key, style);
        }

        foreach (var key in new[]
                 {
                     "App.Button.Primary",
                     "App.Button.Secondary",
                     "App.Button.Subtle",
                     "App.Button.Icon",
                     "App.Button.Danger",
                     "App.Button.DangerIcon",
                     "App.Button.ContextAction"
                 })
        {
            var style = styles[key];
            Assert.Contains(style.Elements(Presentation + "Setter"), setter =>
                (string?)setter.Attribute("Property") == "MouseOverBackground");
            Assert.Contains(style.Elements(Presentation + "Setter"), setter =>
                (string?)setter.Attribute("Property") == "PressedBackground");
            AssertNoDirectInteractionSurfaceSetters(style);
        }

        var media = styles["App.Media.Button"];
        Assert.Contains(media.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "MouseOverBackground");
        Assert.Contains(media.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "PressedBackground");
        AssertNoDirectInteractionSurfaceSetters(media);

        var toolbar = styles["App.Media.ToolbarControl"];
        Assert.Contains(toolbar.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "MouseOverBackground");
        Assert.Contains(toolbar.Elements(Presentation + "Setter"), setter =>
            (string?)setter.Attribute("Property") == "PressedBackground");
        AssertNoDirectInteractionSurfaceSetters(toolbar);

        var interactionHost = styles["App.Button.InteractionHost"];
        Assert.NotEmpty(interactionHost.Descendants(Presentation + "ControlTemplate"));
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void Button_text_and_focus_pairs_meet_contrast_contract(string theme)
    {
        var palette = LoadPalette(theme);
        var textPairs = new[]
        {
            ("App.Brush.Text.Primary", "App.Brush.Surface.Primary"),
            ("App.Brush.Interaction.Foreground.Hover", "App.Brush.Interaction.Surface.Hover"),
            ("App.Brush.Interaction.Foreground.Pressed", "App.Brush.Interaction.Surface.Pressed"),
            ("App.Brush.Accent.Text", "App.Brush.Accent.Default"),
            ("App.Brush.Accent.Text", "App.Brush.Accent.Hover"),
            ("App.Brush.Accent.Text", "App.Brush.Accent.Pressed"),
            ("App.Brush.Danger.Text", "App.Brush.Danger"),
            ("App.Brush.Danger.Text", "App.Brush.Danger.Hover"),
            ("App.Brush.Danger.Pressed.Text", "App.Brush.Danger.Pressed"),
            ("App.Brush.Danger.Subtle.Text", "App.Brush.Danger.Subtle"),
            ("App.Brush.Surface.Secondary.Text", "App.Brush.Surface.Secondary")
        };

        foreach (var (foreground, background) in textPairs)
        {
            Assert.True(
                Contrast(palette[foreground], palette[background]) >= 4.5,
                $"{theme} contrast for {foreground} on {background} is {Contrast(palette[foreground], palette[background]):F2}:1.");
        }

        foreach (var background in new[]
                 {
                     "App.Brush.Surface.Primary",
                     "App.Brush.Surface.Raised",
                     "App.Brush.Surface.Secondary",
                     "App.Brush.Interaction.Surface.Hover",
                     "App.Brush.Interaction.Surface.Pressed"
                 })
        {
            Assert.True(
                Contrast(palette["App.Brush.Focus.OnSurface"], palette[background]) >= 3,
                $"{theme} focus contrast on {background} is below 3:1.");
        }
    }

    [Fact]
    public void High_contrast_button_colors_map_to_the_matching_system_roles()
    {
        var path = Path.Combine(
            Repository.RootPath,
            "src/NovelSpeaker.App/Shared/Theming/Palettes/Palette.HighContrast.xaml");
        var brushes = XDocument.Load(path).Root!.Elements(Presentation + "SolidColorBrush")
            .ToDictionary(brush => (string)brush.Attribute(Xaml + "Key")!, brush => (string)brush.Attribute("Color")!, StringComparer.Ordinal);

        AssertSystemRole(brushes, "App.Brush.Interaction.Surface.Default", "WindowColorKey");
        AssertSystemRole(brushes, "App.Brush.Interaction.Foreground.Hover", "ControlTextColorKey");
        AssertSystemRole(brushes, "App.Brush.Interaction.Foreground.Pressed", "HighlightTextColorKey");
        AssertSystemRole(brushes, "App.Brush.Danger.Foreground", "WindowTextColorKey");
        AssertSystemRole(brushes, "App.Brush.Danger.Subtle.Text", "ControlTextColorKey");
        AssertSystemRole(brushes, "App.Brush.Danger.Pressed.Text", "HighlightTextColorKey");
        AssertSystemRole(brushes, "App.Brush.Focus.OnSurface", "WindowTextColorKey");
    }

    private static void AssertNoDirectInteractionSurfaceSetters(XElement style)
    {
        var forbidden = style.Descendants()
            .Where(element => element.Name.LocalName is "Trigger" or "MultiTrigger")
            .Where(trigger => (string?)trigger.Attribute("Property") is "IsMouseOver" or "IsPressed" ||
                trigger.Descendants(Presentation + "Condition")
                    .Any(condition => (string?)condition.Attribute("Property") is "IsMouseOver" or "IsPressed"))
            .SelectMany(trigger => trigger.Elements(Presentation + "Setter"))
            .Where(setter => (string?)setter.Attribute("Property") is "Background" or "BorderBrush")
            .Select(setter => (string?)setter.Attribute("Property"))
            .ToArray();
        Assert.Empty(forbidden);
    }

    private static Dictionary<string, Rgb> LoadPalette(string theme)
    {
        var path = Path.Combine(
            Repository.RootPath,
            $"src/NovelSpeaker.App/Shared/Theming/Palettes/Palette.{theme}.xaml");
        return XDocument.Load(path).Root!.Elements(Presentation + "SolidColorBrush")
            .Where(brush => brush.Attribute("Color")?.Value.StartsWith('#') == true)
            .ToDictionary(
                brush => (string)brush.Attribute(Xaml + "Key")!,
                brush => ParseColor((string)brush.Attribute("Color")!),
                StringComparer.Ordinal);
    }

    private static Dictionary<string, XElement> ReadStyles(string fileName)
    {
        var path = Path.Combine(
            Repository.RootPath,
            $"src/NovelSpeaker.App/Shared/Theming/Resources/Styles/{fileName}");
        return XDocument.Load(path).Root!.Elements(Presentation + "Style")
            .Where(style => style.Attribute(Xaml + "Key") is not null)
            .ToDictionary(style => (string)style.Attribute(Xaml + "Key")!, StringComparer.Ordinal);
    }

    private static Rgb ParseColor(string value)
    {
        var hex = value.TrimStart('#');
        if (hex.Length == 8)
        {
            hex = hex[2..];
        }

        return new Rgb(
            byte.Parse(hex[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(hex.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(hex.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    }

    private static double Contrast(Rgb foreground, Rgb background)
    {
        var first = Luminance(foreground);
        var second = Luminance(background);
        return (Math.Max(first, second) + 0.05) / (Math.Min(first, second) + 0.05);
    }

    private static double Luminance(Rgb color)
    {
        static double Linear(byte channel)
        {
            var value = channel / 255d;
            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Linear(color.Red)) + (0.7152 * Linear(color.Green)) + (0.0722 * Linear(color.Blue));
    }

    private static void AssertSystemRole(IReadOnlyDictionary<string, string> brushes, string key, string role) =>
        Assert.Contains(role, brushes[key], StringComparison.Ordinal);

    private readonly record struct Rgb(byte Red, byte Green, byte Blue);
}
