using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using Xunit;

namespace NovelSpeaker.App.WpfTests.Ui;

public sealed class InteractionCallerAuditTests
{
    private static readonly XNamespace XamlNamespace =
        "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void Production_interaction_callers_use_explicit_styles_and_single_selection_owner()
    {
        var repositoryRoot = LocateRepositoryRoot();
        var appRoot = Path.Combine(repositoryRoot, "src", "NovelSpeaker.App");
        var allProductionXaml = Directory.EnumerateFiles(appRoot, "*.xaml", SearchOption.AllDirectories)
            .Where(path => !path.Contains(
                Path.Combine("Shared", "Theming", "Palettes"),
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var callerXaml = allProductionXaml
            .Where(path => !path.Contains(
                Path.Combine("Shared", "Theming"),
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var controlThemeXaml = allProductionXaml
            .Where(path => path.Contains(
                Path.Combine("Shared", "Theming", "Resources", "ControlThemes"),
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var styleDefinitions = allProductionXaml
            .SelectMany(path => XDocument.Load(path).Descendants())
            .Where(element => element.Name.LocalName == "Style")
            .Where(element => element.Attribute(XamlNamespace + "Key") is not null)
            .ToDictionary(
                element => (string)element.Attribute(XamlNamespace + "Key")!,
                element => new StyleDefinition(
                    (string)element.Attribute(XamlNamespace + "Key")!,
                    (string?)element.Attribute("BasedOn"),
                    (string?)element.Attribute("TargetType")),
                StringComparer.Ordinal);

        var invalidColorValues = allProductionXaml
            .SelectMany(path => XDocument.Load(path).Descendants()
                .SelectMany(element => element.Attributes()
                    .Select(attribute => new { Path = path, Element = element, Attribute = attribute })))
            .Where(item => IsColorAttribute(item.Attribute))
            .Where(item => !IsAllowedColorValue(item.Attribute))
            .Select(item =>
                $"{Path.GetRelativePath(repositoryRoot, item.Path)} " +
                $"<{item.Element.Name.LocalName}> {item.Attribute.Name.LocalName}=\"{item.Attribute.Value}\"")
            .ToArray();
        Assert.Empty(invalidColorValues);

        /*
         * Transparent is deliberately allowed only for background/chrome
         * properties. All semantic color values must come from App resources
         * or from an explicit view/template binding.
         */
        foreach (var path in callerXaml.Concat(controlThemeXaml).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var document = XDocument.Load(path);
            foreach (var element in document.Descendants().Where(IsInteractiveControl))
            {
                var styleReference = StyleReference(element);
                Assert.True(
                    IsAppStyleReference(styleReference, styleDefinitions),
                    $"{Path.GetRelativePath(repositoryRoot, path)} " +
                    $"<{element.Name.LocalName}> must resolve to an explicit App interaction style, " +
                    $"but was '{styleReference ?? "<none>"}'.");
            }

            if (callerXaml.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                Assert.DoesNotContain(
                    document.Descendants().Where(element => element.Name.LocalName == "Setter"),
                    setter => (string?)setter.Attribute("Value") is
                        "{DynamicResource App.Brush.Interaction.Foreground.Selected}" or
                        "{DynamicResource App.Brush.Interaction.Foreground.Disabled}");
            }
        }

        var selectionOwners = allProductionXaml
            .SelectMany(path => XDocument.Load(path).Descendants())
            .Where(element => IsSelectionSurfaceStyle(
                StyleReference(element),
                styleDefinitions))
            .ToArray();

        var selectionButtonPairs = selectionOwners
            .Select(owner => owner.DescendantsAndSelf()
                .Concat(owner.Ancestors())
                .Where(element => IsFullSurfaceButton(element))
                .Distinct()
                .ToArray())
            .Where(buttons => buttons.Length > 0)
            .ToArray();
        Assert.NotEmpty(selectionButtonPairs);
        Assert.All(
            selectionButtonPairs.SelectMany(buttons => buttons).Distinct(),
            button => Assert.True(
                IsInteractionHostStyle(StyleReference(button), styleDefinitions),
                $"Selection surface related to a full-surface <Button> must use " +
                $"App.Button.InteractionHost, but was " +
                $"'{StyleReference(button) ?? "<none>"}'."));

        var floatingButtons = allProductionXaml
            .SelectMany(path => XDocument.Load(path).Descendants()
                .Where(element => element.Name.LocalName == "Button")
                .Select(button => new { Path = path, Button = button }))
            .Where(item => IsFloatingButtonStyle(StyleReference(item.Button), styleDefinitions))
            .ToArray();
        Assert.NotEmpty(floatingButtons);
        Assert.All(
            floatingButtons,
            item => Assert.True(
                item.Button.Descendants().Any(element =>
                    IsFloatingSurfaceStyle(StyleReference(element), styleDefinitions)),
                $"{Path.GetRelativePath(repositoryRoot, item.Path)} " +
                $"<Button> using App.Button.Floating must contain " +
                $"App.Surface.FloatingAction, but did not: " +
                $"'{StyleReference(item.Button)}'."));

        var rulesPath = Path.Combine(
            appRoot,
            "Shared",
            "Theming",
            "Resources",
            "ControlThemes",
            "Rules.xaml");
        var rulesDocument = XDocument.Load(rulesPath);
        var selectButton = rulesDocument.Descendants().Single(element =>
            element.Name.LocalName == "Button" &&
            (string?)element.Attribute(XamlNamespace + "Name") == "PART_SelectButton");
        Assert.Equal("{StaticResource App.Button.InteractionHost}", StyleReference(selectButton));

        foreach (var path in callerXaml.Where(path =>
                     path.EndsWith("RulesPage.xaml", StringComparison.Ordinal) ||
                     path.EndsWith("RegexReplacementRulesPage.xaml", StringComparison.Ordinal)))
        {
            var document = XDocument.Load(path);
            var dismissOverlay = document.Descendants().Single(element =>
                element.Name.LocalName == "Button" &&
                (string?)element.Attribute(XamlNamespace + "Name") == "HelpDrawerDismissOverlay");
            Assert.Equal("{StaticResource App.Button.InteractionHost}", StyleReference(dismissOverlay));
        }

        var playerDocument = XDocument.Load(Path.Combine(
            appRoot,
            "Features",
            "Playback",
            "Components",
            "PlayerView.xaml"));
        foreach (var inputName in new[] { "CustomStopMinutesTextBox", "SpeedEditorTextBox" })
        {
            var input = playerDocument.Descendants().Single(element =>
                element.Name.LocalName == "TextBox" &&
                (string?)element.Attribute(XamlNamespace + "Name") == inputName);
            Assert.Equal("{StaticResource App.Input.TextBox.Compact}", StyleReference(input));
        }
    }

    private static bool IsInteractiveControl(XElement element) =>
        element.Name.LocalName is "Button" or "TextBox" or "PasswordBox" or "ComboBox" or
            "Slider" or "ToggleSwitch" or "MenuItem" or "ContextMenu" or "Flyout" or
            "Popup" or "ListBoxItem" or "Separator";

    private static bool IsColorAttribute(XAttribute attribute) =>
        attribute.Name.LocalName is "Background" or "Foreground" or "BorderBrush" or "Color" or
            "Fill" or "Stroke" or "MouseOverBackground" or "PressedBackground" or
            "MouseOverBorderBrush" or "PressedBorderBrush";

    private static bool IsAllowedColorValue(XAttribute attribute)
    {
        if (attribute.Value == "Transparent")
        {
            return attribute.Name.LocalName is "Background" or "BorderBrush" or "Color";
        }

        return attribute.Value.StartsWith("{DynamicResource App.", StringComparison.Ordinal) ||
               attribute.Value.StartsWith("{StaticResource App.", StringComparison.Ordinal) ||
               attribute.Value.StartsWith("{Binding", StringComparison.Ordinal) ||
               attribute.Value.StartsWith("{TemplateBinding", StringComparison.Ordinal) ||
               attribute.Value == "{x:Null}";
    }

    private static bool IsFullSurfaceButton(XElement element) =>
        element.Name.LocalName == "Button" &&
        ((string?)element.Attribute("HorizontalAlignment") == "Stretch" ||
         (string?)element.Attribute("VerticalAlignment") == "Stretch");

    private static string? StyleReference(XElement element) =>
        (string?)element.Attribute("Style") ??
        element.Elements().FirstOrDefault(child => child.Name.LocalName.EndsWith(".Style", StringComparison.Ordinal))
            ?.Elements().FirstOrDefault(child => child.Name.LocalName == "Style")
            ?.Attribute("BasedOn")?.Value;

    private static bool IsAppStyleReference(
        string? styleReference,
        IReadOnlyDictionary<string, StyleDefinition> styleDefinitions) =>
        GetStyleKeyChain(styleReference, styleDefinitions)
            .Any(definition => definition.Key.StartsWith("App.", StringComparison.Ordinal) &&
                              !string.IsNullOrWhiteSpace(definition.TargetType));

    private static bool IsSelectionSurfaceStyle(
        string? styleReference,
        IReadOnlyDictionary<string, StyleDefinition> styleDefinitions) =>
        GetStyleKeyChain(styleReference, styleDefinitions)
            .Any(definition => definition.Key.StartsWith("App.Selection.", StringComparison.Ordinal) &&
                              !definition.Key.StartsWith("App.Selection.Content.", StringComparison.Ordinal));

    private static bool IsFloatingButtonStyle(
        string? styleReference,
        IReadOnlyDictionary<string, StyleDefinition> styleDefinitions) =>
        GetStyleKeyChain(styleReference, styleDefinitions)
            .Any(definition => definition.Key == "App.Button.Floating");

    private static bool IsInteractionHostStyle(
        string? styleReference,
        IReadOnlyDictionary<string, StyleDefinition> styleDefinitions) =>
        GetStyleKeyChain(styleReference, styleDefinitions)
            .Any(definition => definition.Key == "App.Button.InteractionHost");

    private static bool IsFloatingSurfaceStyle(
        string? styleReference,
        IReadOnlyDictionary<string, StyleDefinition> styleDefinitions) =>
        GetStyleKeyChain(styleReference, styleDefinitions)
            .Any(definition => definition.Key == "App.Surface.FloatingAction");

    private static IReadOnlyList<StyleDefinition> GetStyleKeyChain(
        string? styleReference,
        IReadOnlyDictionary<string, StyleDefinition> styleDefinitions)
    {
        var definitions = new List<StyleDefinition>();
        var visitedKeys = new HashSet<string>(StringComparer.Ordinal);
        var key = ExtractStaticResourceKey(styleReference);
        while (key is not null)
        {
            if (!visitedKeys.Add(key))
            {
                return [];
            }

            if (!styleDefinitions.TryGetValue(key, out var definition))
            {
                return [];
            }

            definitions.Add(definition);
            if (definition.Key.StartsWith("Provider.", StringComparison.Ordinal))
            {
                // Provider.* is the explicit bridge to Wpf.Ui's implicit type style.
                break;
            }

            key = ExtractStaticResourceKey(definition.BasedOn);
        }

        return definitions;
    }

    private static string? ExtractStaticResourceKey(string? resourceReference)
    {
        const string prefix = "{StaticResource ";
        if (resourceReference is null ||
            !resourceReference.StartsWith(prefix, StringComparison.Ordinal) ||
            !resourceReference.EndsWith('}'))
        {
            return null;
        }

        return resourceReference[prefix.Length..^1];
    }

    private sealed record StyleDefinition(string Key, string? BasedOn, string? TargetType);

    private static string LocateRepositoryRoot()
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
