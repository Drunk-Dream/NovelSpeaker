using System.IO;
using System.Xml;
using System.Xml.Linq;
using Xunit;

namespace NovelSpeaker.App.WpfTests.Architecture;

public sealed class IconForegroundContractTests
{
    private static readonly XNamespace XamlNamespace =
        "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly XNamespace WpfNamespace =
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace WpfUiNamespace =
        "http://schemas.lepo.co/wpfui/2022/xaml";

    [Fact]
    public void Standalone_icon_styles_define_the_semantic_foreground_contract()
    {
        var path = Path.Combine(
            LocateRepositoryRoot(),
            "src",
            "NovelSpeaker.App",
            "Shared",
            "Theming",
            "Resources",
            "Styles",
            "Icons.xaml");
        var document = XDocument.Load(path);
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["App.Icon.Primary"] = "{DynamicResource App.Brush.Text.Primary}",
            ["App.Icon.Secondary"] = "{DynamicResource App.Brush.Text.Secondary}",
            ["App.Icon.Accent"] = "{DynamicResource App.Brush.Accent.Default}",
            ["App.Icon.Danger"] = "{DynamicResource App.Brush.Danger}"
        };

        var styles = document.Root?.Elements()
            .Where(element => element.Name.LocalName == "Style")
            .ToDictionary(
                element => (string?)element.Attribute(XamlNamespace + "Key") ?? string.Empty,
                StringComparer.Ordinal)
            ?? [];

        Assert.Equal(expected.Keys.Order(StringComparer.Ordinal), styles.Keys.Order(StringComparer.Ordinal));
        foreach (var (key, foreground) in expected)
        {
            var style = styles[key];
            Assert.Equal("{x:Type ui:SymbolIcon}", (string?)style.Attribute("TargetType"));
            Assert.Contains(
                style.Elements(),
                element => element.Name.LocalName == "Setter" &&
                           (string?)element.Attribute("Property") == "Foreground" &&
                           (string?)element.Attribute("Value") == foreground);
        }
    }

    [Fact]
    public void Icon_button_styles_are_owned_by_wpf_ui_buttons_and_use_the_icon_slot()
    {
        var appRoot = Path.Combine(LocateRepositoryRoot(), "src", "NovelSpeaker.App");
        var violations = new List<string>();

        foreach (var path in Directory.EnumerateFiles(appRoot, "*.xaml", SearchOption.AllDirectories))
        {
            var document = XDocument.Load(path, LoadOptions.SetLineInfo);
            foreach (var element in document.Descendants())
            {
                if (!UsesIconButtonStyle(element))
                {
                    continue;
                }

                if (element.Name != WpfUiNamespace + "Button")
                {
                    violations.Add(Describe(path, element, "icon style must be hosted by ui:Button"));
                    continue;
                }

                if (element.Attribute("Foreground") is not null)
                {
                    violations.Add(Describe(
                        path,
                        element,
                        "icon button foreground must be owned by its shared button style"));
                }

                var hasIconAttribute = element.Attribute("Icon") is not null;
                var hasIconPropertyElement = element.Elements().Any(child =>
                    child.Name == WpfUiNamespace + "Button.Icon");
                if (!hasIconAttribute && !hasIconPropertyElement)
                {
                    violations.Add(Describe(path, element, "icon button must use Button.Icon instead of Content"));
                }

                if (element.Elements().Any(child => child.Name == WpfUiNamespace + "SymbolIcon"))
                {
                    violations.Add(Describe(path, element, "direct SymbolIcon content bypasses the owner foreground contract"));
                }
            }

            foreach (var styleElement in document.Descendants().Where(element => element.Name.LocalName == "Style"))
            {
                var basedOn = (string?)styleElement.Attribute("BasedOn") ?? string.Empty;
                if (!basedOn.Contains("App.Button.Icon", StringComparison.Ordinal))
                {
                    continue;
                }

                if ((string?)styleElement.Attribute("TargetType") != "{x:Type ui:Button}")
                {
                    violations.Add(Describe(path, styleElement, "styles based on App.Button.Icon must target ui:Button"));
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            $"Icon button foreground contract violations:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }

    [Fact]
    public void Product_symbol_icons_do_not_override_owner_foreground_locally()
    {
        var appRoot = Path.Combine(LocateRepositoryRoot(), "src", "NovelSpeaker.App");
        var legacySegment = Path.Combine("Shared", "Theming", "Resources", "Legacy");
        var violations = new List<string>();

        foreach (var path in Directory.EnumerateFiles(appRoot, "*.xaml", SearchOption.AllDirectories)
                     .Where(path => !path.Contains(legacySegment, StringComparison.Ordinal)))
        {
            var document = XDocument.Load(path, LoadOptions.SetLineInfo);
            foreach (var icon in document.Descendants(WpfUiNamespace + "SymbolIcon"))
            {
                if (icon.Attribute("Foreground") is not null)
                {
                    violations.Add(Describe(path, icon, "SymbolIcon.Foreground must come from its owner or App.Icon.* style"));
                }

                var localForegroundSetter = icon.Descendants()
                    .Any(element => element.Name.LocalName == "Setter" &&
                                    (string?)element.Attribute("Property") == "Foreground");
                if (localForegroundSetter)
                {
                    violations.Add(Describe(path, icon, "local SymbolIcon foreground setters are not allowed"));
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            $"Local SymbolIcon foreground overrides found:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }

    [Fact]
    public void App_owned_standalone_symbol_icons_use_semantic_icon_styles()
    {
        var appRoot = Path.Combine(LocateRepositoryRoot(), "src", "NovelSpeaker.App");
        var legacySegment = Path.Combine("Shared", "Theming", "Resources", "Legacy");
        var violations = new List<string>();

        foreach (var path in Directory.EnumerateFiles(appRoot, "*.xaml", SearchOption.AllDirectories)
                     .Where(path => !path.Contains(legacySegment, StringComparison.Ordinal)))
        {
            var document = XDocument.Load(path, LoadOptions.SetLineInfo);
            foreach (var icon in document.Descendants(WpfUiNamespace + "SymbolIcon"))
            {
                if (icon.Ancestors().Any(ancestor =>
                        ancestor.Name == WpfNamespace + "Button" ||
                        ancestor.Name == WpfUiNamespace + "Button"))
                {
                    continue;
                }

                if (icon.Ancestors().Any(ancestor => ancestor.Name == WpfUiNamespace + "NavigationViewItem.Icon"))
                {
                    continue;
                }

                var directStyle = (string?)icon.Attribute("Style") ?? string.Empty;
                var localSemanticBase = icon.Elements()
                    .Where(child => child.Name == WpfUiNamespace + "SymbolIcon.Style")
                    .Descendants()
                    .Where(child => child.Name.LocalName == "Style")
                    .Select(child => (string?)child.Attribute("BasedOn") ?? string.Empty)
                    .Any(value => value.Contains("App.Icon.", StringComparison.Ordinal));
                if (!directStyle.Contains("App.Icon.", StringComparison.Ordinal) && !localSemanticBase)
                {
                    violations.Add(Describe(
                        path,
                        icon,
                        "app-owned standalone SymbolIcon must use an App.Icon.* semantic style"));
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            $"Standalone SymbolIcon semantic style violations:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }

    private static bool UsesIconButtonStyle(XElement element)
    {
        var directStyle = (string?)element.Attribute("Style") ?? string.Empty;
        if (ContainsIconButtonStyleKey(directStyle))
        {
            return true;
        }

        return element.Elements()
            .Where(child => child.Name == WpfUiNamespace + "Button.Style")
            .Descendants()
            .Where(child => child.Name.LocalName == "Style")
            .Select(child => (string?)child.Attribute("BasedOn") ?? string.Empty)
            .Any(ContainsIconButtonStyleKey);
    }

    private static bool ContainsIconButtonStyleKey(string value) =>
        value.Contains("App.Button.Icon", StringComparison.Ordinal) ||
        value.Contains("App.Button.DangerIcon", StringComparison.Ordinal) ||
        value.Contains("App.Media.Button", StringComparison.Ordinal);

    private static string Describe(string path, XElement element, string message)
    {
        var lineInfo = (IXmlLineInfo)element;
        return $"{Path.GetRelativePath(LocateRepositoryRoot(), path)}:{lineInfo.LineNumber} {message}";
    }

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
