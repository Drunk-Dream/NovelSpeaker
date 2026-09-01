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
            .Where(item => IsFloatingIconButtonStyle(StyleReference(item.Button), styleDefinitions))
            .ToArray();
        Assert.Equal(3, floatingButtons.Length);
        Assert.All(
            floatingButtons,
            item =>
            {
                Assert.DoesNotContain(item.Button.Descendants(), element => element.Name.LocalName == "Border");
                Assert.True(
                    item.Button.Attribute("Icon") is not null ||
                    item.Button.Elements().Any(element => element.Name.LocalName == "Button.Icon"));
            });

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

    [Fact]
    public void Production_list_containers_and_floating_actions_keep_their_owning_semantics()
    {
        var repositoryRoot = LocateRepositoryRoot();
        var appRoot = Path.Combine(repositoryRoot, "src", "NovelSpeaker.App");
        var productionXaml = Directory.EnumerateFiles(appRoot, "*.xaml", SearchOption.AllDirectories)
            .Where(path => !path.Contains(
                Path.Combine("Shared", "Theming", "Palettes"),
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var callerXaml = productionXaml
            .Where(path => !path.Contains(
                Path.Combine("Shared", "Theming"),
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var nonCanonicalContainerXaml = productionXaml
            .Where(path => !path.EndsWith(
                Path.Combine("Shared", "Theming", "Resources", "Styles", "Selection.xaml"),
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var styleDefinitions = productionXaml
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

        foreach (var path in productionXaml)
        {
            var document = XDocument.Load(path);
            foreach (var listBox in document.Descendants().Where(element => element.Name.LocalName == "ListBox"))
            {
                var itemTemplate = listBox.Elements()
                    .SingleOrDefault(element => element.Name.LocalName == "ListBox.ItemTemplate");
                if (itemTemplate is null)
                {
                    continue;
                }

                var itemContainerStyle = listBox.Elements()
                    .SingleOrDefault(element => element.Name.LocalName == "ListBox.ItemContainerStyle");
                Assert.True(
                    itemContainerStyle is not null,
                    $"{Path.GetRelativePath(repositoryRoot, path)} contains a templated ListBox without " +
                    "an explicit chrome-free item-container style.");

                var style = itemContainerStyle!.Elements()
                    .SingleOrDefault(element => element.Name.LocalName == "Style");
                Assert.NotNull(style);
                Assert.True(
                    GetStyleKeyChain((string?)style!.Attribute("BasedOn"), styleDefinitions)
                        .Any(definition => definition.Key == "App.Selection.ChromeFreeItemContainer"),
                    $"{Path.GetRelativePath(repositoryRoot, path)} ListBox item containers must inherit " +
                    "App.Selection.ChromeFreeItemContainer.");
            }
        }

        var duplicatedContainerTemplates = nonCanonicalContainerXaml
            .SelectMany(path => XDocument.Load(path).Descendants()
                .Where(element =>
                    (element.Name.LocalName == "ControlTemplate" &&
                     IsListBoxItemTargetType((string?)element.Attribute("TargetType"))) ||
                    (element.Name.LocalName == "Style" &&
                     IsListBoxItemTargetType((string?)element.Attribute("TargetType")) &&
                     element.Descendants().Any(descendant => descendant.Name.LocalName == "Setter" &&
                        (string?)descendant.Attribute("Property") == "Template")))
                .Select(_ => Path.GetRelativePath(repositoryRoot, path)))
            .ToArray();
        Assert.Empty(duplicatedContainerTemplates);

        var floatingButtons = productionXaml
            .SelectMany(path => XDocument.Load(path).Descendants()
                .Where(element => element.Name.LocalName == "Button")
                .Where(element => IsFloatingIconButtonStyle(StyleReference(element), styleDefinitions))
                .Select(button => (
                    Path: Path.GetRelativePath(repositoryRoot, path),
                    Name: (string?)button.Attribute(XamlNamespace + "Name"),
                    Button: button)))
            .ToArray();
        var expectedFloatingCallers = new HashSet<(string Path, string Name)>
        {
            (Path.Combine("src", "NovelSpeaker.App", "Features", "BookDetails", "BookDetailsPage.xaml"),
                "LocateCurrentChapterButton"),
            (Path.Combine("src", "NovelSpeaker.App", "Features", "Playback", "Components", "PlayerView.xaml"),
                "LocateCurrentChapterButton"),
            (Path.Combine("src", "NovelSpeaker.App", "Features", "Playback", "Components", "PlayerView.xaml"),
                "ReturnToCurrentSegmentButton")
        };
        Assert.Equal(
            expectedFloatingCallers,
            floatingButtons.Select(item => (item.Path, item.Name!)).ToHashSet());
        Assert.All(
            floatingButtons,
            item =>
            {
                Assert.DoesNotContain(item.Button.Descendants(), element => element.Name.LocalName == "Border");
                Assert.True(
                    item.Button.Attribute("Icon") is not null ||
                    item.Button.Elements().Any(element => element.Name.LocalName == "Button.Icon"));
            });
    }

    private static bool IsListBoxItemTargetType(string? targetType) =>
        targetType is "ListBoxItem" or "{x:Type ListBoxItem}";

    private static IEnumerable<XElement> ButtonContentNodes(XElement button)
    {
        var contentProperty = button.Elements()
            .SingleOrDefault(element => element.Name.LocalName == "Button.Content");
        if (contentProperty is not null)
        {
            return contentProperty.DescendantsAndSelf();
        }

        return button.Elements()
            .Where(element => !element.Name.LocalName.StartsWith("Button.", StringComparison.Ordinal))
            .SelectMany(element => element.DescendantsAndSelf());
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

    private static bool IsFloatingIconButtonStyle(
        string? styleReference,
        IReadOnlyDictionary<string, StyleDefinition> styleDefinitions) =>
        GetStyleKeyChain(styleReference, styleDefinitions)
            .Any(definition => definition.Key == "App.Button.FloatingIcon");

    private static bool IsInteractionHostStyle(
        string? styleReference,
        IReadOnlyDictionary<string, StyleDefinition> styleDefinitions) =>
        GetStyleKeyChain(styleReference, styleDefinitions)
            .Any(definition => definition.Key == "App.Button.InteractionHost");

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
