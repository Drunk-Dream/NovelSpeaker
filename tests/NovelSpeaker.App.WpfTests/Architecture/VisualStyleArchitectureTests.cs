using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Xunit;

namespace NovelSpeaker.App.WpfTests.Architecture;

public sealed class VisualStyleArchitectureTests
{
    private static readonly string[] StableDesignTokenKeys =
    [
        "App.Space.4",
        "App.Space.8",
        "App.Space.12",
        "App.Space.16",
        "App.Space.20",
        "App.Space.24",
        "App.Space.32",
        "App.Space.40",
        "App.Space.48",
        "App.Radius.Small",
        "App.Radius.Medium",
        "App.Radius.Large",
        "App.Size.Icon.Small",
        "App.Size.Icon.Standard",
        "App.Size.Icon.Large",
        "App.Size.Icon.Touch",
        "App.Size.MediaButton",
        "App.Size.Control.Compact",
        "App.Size.Control.Standard",
        "App.Opacity.Disabled",
        "App.Text.Family.Ui",
        "App.Text.Size.PageTitle",
        "App.Text.Size.SectionTitle",
        "App.Text.Size.ItemTitle",
        "App.Text.Size.Body",
        "App.Text.Size.Secondary",
        "App.Text.Size.Caption",
        "App.Text.Weight.Regular",
        "App.Text.Weight.SemiBold",
        "App.Text.LineHeight.Body",
        "App.Text.LineHeight.Secondary",
        "App.Motion.Fast",
        "App.Motion.Standard",
        "App.Motion.Slow",
        "App.Elevation.Low",
        "App.Elevation.Medium",
        "App.Elevation.High"
    ];

    [Fact]
    public void Current_application_style_ownership_has_no_violations()
    {
        var repositoryRoot = LocateRepositoryRoot();
        var audit = VisualStyleOwnershipScanner.ScanRepository(repositoryRoot);

        Assert.Empty(audit.Violations);
        Assert.Contains(audit.Provider, entry => entry.Provider == "Wpf.Ui" && entry.Resource == "ThemesDictionary");
        Assert.DoesNotContain(
            audit.GlobalDictionaries,
            entry => entry.Source.Contains("Legacy", StringComparison.OrdinalIgnoreCase));
        Assert.NotEmpty(audit.TemplateOverrides);
        Assert.NotEmpty(audit.PageLocalResources);

        var secondAudit = VisualStyleOwnershipScanner.ScanRepository(repositoryRoot);
        Assert.Equal(audit.ToJson(), secondAudit.ToJson());

    }

    private void Implicit_global_standard_control_style_fixture_is_rejected()
    {
        var result = VisualStyleOwnershipScanner.ScanGlobalDictionary(
            "fixture/ImplicitStyle.xaml",
            XDocument.Parse(
                """
                <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                    <Style TargetType="Button">
                        <Setter Property="Padding" Value="0" />
                    </Style>
                </ResourceDictionary>
                """,
                LoadOptions.SetLineInfo));

        Assert.Contains(
            result.Violations,
            violation => violation.Rule == "implicit-standard-control-style");
    }

    private void Global_standard_control_template_fixture_is_rejected_without_an_explicit_whitelist_entry()
    {
        var result = VisualStyleOwnershipScanner.ScanGlobalDictionary(
            "fixture/Template.xaml",
            XDocument.Parse(
                """
                <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                    <ControlTemplate x:Key="UnapprovedButtonTemplate" TargetType="Button">
                        <Border />
                    </ControlTemplate>
                </ResourceDictionary>
                """,
                LoadOptions.SetLineInfo));

        Assert.Contains(
            result.Violations,
            violation => violation.Rule == "global-standard-control-template");
    }

    private void Theme_runtime_typed_resource_write_fixture_is_rejected()
    {
        var violations = VisualStyleOwnershipScanner.ScanThemeRuntime(
            "fixture/ThemeRuntime.cs",
            """
            using System.Windows;

            public static class Fixture
            {
                public static void Apply()
                {
                    Application.Current.Resources[typeof(Style)] = new Style();
                    Application.Current.Resources[typeof(ControlTemplate)] = new ControlTemplate();
                }
            }
            """);

        Assert.Contains(
            violations,
            violation => violation.Rule == "theme-runtime-resource-write");
    }

    private void Page_specific_design_token_fixture_is_rejected()
    {
        var result = VisualStyleOwnershipScanner.ScanDesignTokens(
            "fixture/DesignTokens.xaml",
            XDocument.Parse(
                """
                <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                    xmlns:sys="clr-namespace:System;assembly=mscorlib">
                    <sys:Double x:Key="WorkbenchListWidth">300</sys:Double>
                    <sys:Double x:Key="RuleActionGap">8</sys:Double>
                </ResourceDictionary>
                """,
                LoadOptions.SetLineInfo));

        Assert.Contains(
            result.Violations,
            violation => violation.Rule == "page-specific-design-token");
        Assert.Equal(2, result.ForbiddenDesignTokens.Count);
    }

    [Fact]
    public void Visual_style_fixture_contracts_reject_global_ownership_violations()
    {
        Implicit_global_standard_control_style_fixture_is_rejected();
        Global_standard_control_template_fixture_is_rejected_without_an_explicit_whitelist_entry();
        Theme_runtime_typed_resource_write_fixture_is_rejected();
        Page_specific_design_token_fixture_is_rejected();
    }

    [Fact]
    public void Stable_design_tokens_have_cross_component_names_without_page_geometry()
    {
        var repositoryRoot = LocateRepositoryRoot();
        var tokensDirectory = Path.Combine(
            repositoryRoot,
            "src",
            "NovelSpeaker.App",
            "Shared",
            "Theming",
            "Resources",
            "Tokens");
        var tokenFiles = Directory
            .EnumerateFiles(tokensDirectory, "*.xaml", SearchOption.TopDirectoryOnly)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var designTokens = tokenFiles
            .SelectMany(path => VisualStyleOwnershipScanner.ScanDesignTokens(
                Path.GetRelativePath(repositoryRoot, path).Replace(Path.DirectorySeparatorChar, '/'),
                XDocument.Load(path, LoadOptions.SetLineInfo)).DesignTokens)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            StableDesignTokenKeys.Order(StringComparer.Ordinal),
            StableDesignTokenKeys
                .Where(designTokens.Contains)
                .Order(StringComparer.Ordinal));
        Assert.Equal(
            StableDesignTokenKeys.Length,
            StableDesignTokenKeys.Count(designTokens.Contains));
        Assert.DoesNotContain(
            StableDesignTokenKeys,
            key => key.Contains("Width", StringComparison.Ordinal) ||
                   key.Contains("Margin", StringComparison.Ordinal) ||
                   key.Contains("Column", StringComparison.Ordinal) ||
                   key.Contains("List", StringComparison.Ordinal));
    }

    [Fact]
    public void Abandoned_visual_asset_audit_files_are_not_present()
    {
        var repositoryRoot = LocateRepositoryRoot();

        Assert.False(File.Exists(Path.Combine(repositoryRoot, "docs", "VISUAL_ASSET_AUDIT.md")));
        Assert.False(File.Exists(Path.Combine(repositoryRoot, "docs", "VISUAL_ASSET_AUDIT.json")));
    }

    private static string LocateRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "src")) &&
                Directory.Exists(Path.Combine(current.FullName, "docs")) &&
                File.Exists(Path.Combine(current.FullName, "NovelSpeaker.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the NovelSpeaker repository root.");
    }
}

internal static class VisualStyleOwnershipScanner
{
    private const string XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";
    private const string WpfUiNamespace = "http://schemas.lepo.co/wpfui/2022/xaml";
    private const string WpfUiComponentPrefix = "/NovelSpeaker;component/";

    private static readonly HashSet<string> StandardWpfTargetTypes =
    [
        "Button",
        "CheckBox",
        "ComboBox",
        "ComboBoxItem",
        "ListBox",
        "ListBoxItem",
        "PasswordBox",
        "ProgressBar",
        "RepeatButton",
        "ScrollViewer",
        "Slider",
        "TextBox",
        "Thumb",
        "ToggleButton",
        "TreeView",
        "TreeViewItem"
    ];

    // These are existing, explicitly keyed application-owned variants. They are
    // recorded in the manifest and are the only global template overrides accepted
    // by this guard. New component templates must move to a local component scope.
    private static readonly HashSet<string> ExplicitGlobalTemplateWhitelist =
    [
        "BorderlessIconButtonStyle",
        "BorderlessListItemButtonStyle",
        "IconButtonControlTemplate",
        "MediaIconButtonControlTemplate",
        "MediaIconButtonStyle",
        "PlaybackProgressSliderStyle",
        "PlaybackSliderThumbStyle",
        "PlaybackSliderTrackButtonStyle",
        "BorderlessListItemButtonControlTemplate",
        // ComboBox is the approved control-family template exception documented
        // in the visual system; its Compact and item variants share this family.
        "App.Input.ComboBox.Standard",
        "App.Input.ComboBox.Item"
    ];

    private static readonly HashSet<string> ForbiddenDesignTokenNames =
    [
        "PagePaneWidth",
        "SettingsControlWidth",
        "WorkbenchListWidth",
        "RuleActionGap"
    ];

    private static readonly Regex ApplicationResourceWrite = new(
        @"Application(?:\.Current)?(?:\?\.)?\.Resources",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex TypedStyleResourceWrite = new(
        @"(?:typeof\s*\(\s*(?:Style|ControlTemplate)\s*\)|new\s+(?:Style|ControlTemplate)\b|(?:Style|ControlTemplate)Key)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static StyleOwnershipAudit ScanRepository(string repositoryRoot)
    {
        var appRoot = Path.Combine(repositoryRoot, "src", "NovelSpeaker.App");
        var appPath = Path.Combine(appRoot, "Bootstrap", "App.xaml");
        var appRelativePath = ToRepositoryRelativePath(repositoryRoot, appPath);
        var appDocument = XDocument.Load(appPath, LoadOptions.SetLineInfo);

        var providers = new List<ProviderResourceEntry>();
        var globalDictionaries = new List<GlobalDictionaryEntry>
        {
            new(appRelativePath, "Application.Resources", true)
        };
        var implicitStyles = new List<StyleFinding>();
        var templateOverrides = new List<TemplateFinding>();
        var violations = new List<StyleOwnershipViolation>();

        var mergedDictionaries = appDocument
            .Descendants()
            .Where(element => element.Parent?.Name.LocalName.EndsWith(
                                  "MergedDictionaries",
                                  StringComparison.Ordinal) == true)
            .ToArray();

        foreach (var dictionary in mergedDictionaries)
        {
            var source = (string?)dictionary.Attribute("Source");
            if (dictionary.Name.NamespaceName == WpfUiNamespace)
            {
                providers.Add(new ProviderResourceEntry(
                    "Wpf.Ui",
                    dictionary.Name.LocalName,
                    appRelativePath,
                    (string?)dictionary.Attribute("Theme")));
                continue;
            }

            if (string.IsNullOrWhiteSpace(source))
            {
                continue;
            }

            var sourcePath = ResolveResourcePath(repositoryRoot, source);
            var sourceRelativePath = ToRepositoryRelativePath(repositoryRoot, sourcePath);
            var exists = File.Exists(sourcePath);
            globalDictionaries.Add(new GlobalDictionaryEntry(sourceRelativePath, "Application.Resources", exists));
            if (!exists)
            {
                violations.Add(new StyleOwnershipViolation(
                    "global-resource-dictionary",
                    sourceRelativePath,
                    0,
                    "Application.xaml references a resource dictionary that does not exist."));
                continue;
            }

            var result = ScanGlobalDictionary(sourceRelativePath, XDocument.Load(sourcePath, LoadOptions.SetLineInfo));
            implicitStyles.AddRange(result.ImplicitStyles);
            templateOverrides.AddRange(result.TemplateOverrides);
            violations.AddRange(result.Violations);
        }

        var tokensDirectory = Path.Combine(
            appRoot,
            "Shared",
            "Theming",
            "Resources",
            "Tokens");
        var designTokenResults = Directory
            .EnumerateFiles(tokensDirectory, "*.xaml", SearchOption.TopDirectoryOnly)
            .Order(StringComparer.Ordinal)
            .Select(path => ScanDesignTokens(
                ToRepositoryRelativePath(repositoryRoot, path),
                XDocument.Load(path, LoadOptions.SetLineInfo)))
            .ToArray();
        violations.AddRange(designTokenResults.SelectMany(result => result.Violations));

        var themeRuntimeViolations = new List<RuntimeFinding>();
        var themingRoot = Path.Combine(appRoot, "Shared", "Theming");
        foreach (var sourcePath in Directory.EnumerateFiles(themingRoot, "*.cs", SearchOption.AllDirectories))
        {
            var sourceRelativePath = ToRepositoryRelativePath(repositoryRoot, sourcePath);
            themeRuntimeViolations.AddRange(
                ScanThemeRuntime(sourceRelativePath, File.ReadAllText(sourcePath)));
        }

        violations.AddRange(themeRuntimeViolations.Select(finding =>
            new StyleOwnershipViolation(
                finding.Rule,
                finding.Source,
                finding.Line,
                finding.Detail)));

        var pageLocalResources = ScanPageLocalResources(repositoryRoot, appRoot);

        return new StyleOwnershipAudit(
            "1",
            "2 - style ownership guard",
            ReadGitCommit(repositoryRoot),
            providers.OrderBy(entry => entry.Source, StringComparer.Ordinal)
                .ThenBy(entry => entry.Resource, StringComparer.Ordinal)
                .ToArray(),
            globalDictionaries.OrderBy(entry => entry.Source, StringComparer.Ordinal).ToArray(),
            implicitStyles.OrderBy(entry => entry.Source, StringComparer.Ordinal)
                .ThenBy(entry => entry.Line)
                .ToArray(),
            templateOverrides.OrderBy(entry => entry.Source, StringComparer.Ordinal)
                .ThenBy(entry => entry.Line)
                .ThenBy(entry => entry.ResourceKey, StringComparer.Ordinal)
                .ToArray(),
            pageLocalResources.OrderBy(entry => entry.Source, StringComparer.Ordinal)
                .ThenBy(entry => entry.Line)
                .ThenBy(entry => entry.Kind, StringComparer.Ordinal)
                .ToArray(),
            designTokenResults.SelectMany(result => result.DesignTokens)
                .Order(StringComparer.Ordinal)
                .ToArray(),
            designTokenResults.SelectMany(result => result.ForbiddenDesignTokens)
                .OrderBy(token => token.Key, StringComparer.Ordinal)
                .ToArray(),
            themeRuntimeViolations.OrderBy(entry => entry.Source, StringComparer.Ordinal)
                .ThenBy(entry => entry.Line)
                .ToArray(),
            violations.OrderBy(entry => entry.Rule, StringComparer.Ordinal)
                .ThenBy(entry => entry.Source, StringComparer.Ordinal)
                .ThenBy(entry => entry.Line)
                .ToArray());
    }

    public static GlobalDictionaryScanResult ScanGlobalDictionary(string source, XDocument document)
    {
        var implicitStyles = new List<StyleFinding>();
        var templateOverrides = new List<TemplateFinding>();
        var violations = new List<StyleOwnershipViolation>();
        var root = document.Root;

        if (root is null)
        {
            return new GlobalDictionaryScanResult(implicitStyles, templateOverrides, violations);
        }

        foreach (var style in root.Elements().Where(element => element.Name.LocalName == "Style"))
        {
            var resourceKey = GetKey(style);
            var targetType = NormalizeTargetType((string?)style.Attribute("TargetType"));
            if (resourceKey is null && IsStandardControl(targetType))
            {
                var finding = new StyleFinding(source, GetLine(style), resourceKey, targetType);
                implicitStyles.Add(finding);
                violations.Add(new StyleOwnershipViolation(
                    "implicit-standard-control-style",
                    source,
                    GetLine(style),
                    $"Global style for '{targetType}' has no explicit x:Key."));
            }

            if (!IsStandardControl(targetType))
            {
                continue;
            }

            var templateSetter = style
                .Elements()
                .Where(element => element.Name.LocalName == "Setter")
                .FirstOrDefault(element =>
                    string.Equals((string?)element.Attribute("Property"), "Template", StringComparison.Ordinal));
            if (templateSetter is null)
            {
                continue;
            }

            var approved = resourceKey is not null && ExplicitGlobalTemplateWhitelist.Contains(resourceKey);
            var templateFinding = new TemplateFinding(
                source,
                GetLine(style),
                resourceKey,
                targetType,
                "style-template-setter",
                approved);
            templateOverrides.Add(templateFinding);
            if (!approved)
            {
                violations.Add(new StyleOwnershipViolation(
                    "global-standard-control-template",
                    source,
                    GetLine(style),
                    $"Global style '{resourceKey ?? "<implicit>"}' replaces the '{targetType}' template without an explicit whitelist entry."));
            }
        }

        foreach (var template in root.Elements().Where(element => element.Name.LocalName == "ControlTemplate"))
        {
            var resourceKey = GetKey(template);
            var targetType = NormalizeTargetType((string?)template.Attribute("TargetType"));
            if (!IsStandardControl(targetType))
            {
                continue;
            }

            var approved = resourceKey is not null && ExplicitGlobalTemplateWhitelist.Contains(resourceKey);
            templateOverrides.Add(new TemplateFinding(
                source,
                GetLine(template),
                resourceKey,
                targetType,
                "control-template-resource",
                approved));
            if (!approved)
            {
                violations.Add(new StyleOwnershipViolation(
                    "global-standard-control-template",
                    source,
                    GetLine(template),
                    $"Global '{targetType}' ControlTemplate '{resourceKey ?? "<implicit>"}' is not explicitly whitelisted."));
            }
        }

        return new GlobalDictionaryScanResult(implicitStyles, templateOverrides, violations);
    }

    public static IReadOnlyList<RuntimeFinding> ScanThemeRuntime(string source, string content)
    {
        var findings = new List<RuntimeFinding>();
        var lines = content.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (ApplicationResourceWrite.IsMatch(line) && TypedStyleResourceWrite.IsMatch(line))
            {
                findings.Add(new RuntimeFinding(
                    "theme-runtime-resource-write",
                    source,
                    index + 1,
                    "Theme runtime writes a Style or ControlTemplate resource to Application.Resources."));
            }
        }

        return findings;
    }

    public static DesignTokenScanResult ScanDesignTokens(string source, XDocument document)
    {
        var designTokens = new List<string>();
        var forbiddenDesignTokens = new List<TokenFinding>();
        var violations = new List<StyleOwnershipViolation>();

        foreach (var resource in document.Root?.Elements() ?? [])
        {
            var key = GetKey(resource);
            if (key is null)
            {
                continue;
            }

            designTokens.Add(key);
            if (!ForbiddenDesignTokenNames.Contains(key))
            {
                continue;
            }

            var finding = new TokenFinding(source, GetLine(resource), key);
            forbiddenDesignTokens.Add(finding);
            violations.Add(new StyleOwnershipViolation(
                "page-specific-design-token",
                source,
                GetLine(resource),
                $"Global Design Token '{key}' is page-specific geometry."));
        }

        return new DesignTokenScanResult(
            designTokens.Order(StringComparer.Ordinal).ToArray(),
            forbiddenDesignTokens.OrderBy(token => token.Key, StringComparer.Ordinal).ToArray(),
            violations);
    }

    private static IReadOnlyList<PageLocalResourceEntry> ScanPageLocalResources(
        string repositoryRoot,
        string appRoot)
    {
        var resources = new List<PageLocalResourceEntry>();
        foreach (var xamlPath in Directory.EnumerateFiles(appRoot, "*.xaml", SearchOption.AllDirectories)
                     .Where(path => !IsGlobalResourceDictionary(path) &&
                                    !path.EndsWith(Path.Combine("Bootstrap", "App.xaml"), StringComparison.OrdinalIgnoreCase)))
        {
            var document = XDocument.Load(xamlPath, LoadOptions.SetLineInfo);
            var source = ToRepositoryRelativePath(repositoryRoot, xamlPath);

            foreach (var resourceScope in document.Descendants().Where(element => element.Name.LocalName == "Resources"))
            {
                foreach (var resource in resourceScope.Elements())
                {
                    resources.Add(CreatePageResourceEntry(source, resource, resourceScope.Parent?.Name.LocalName ?? "Resources"));
                }
            }

            foreach (var resource in document.Descendants().Where(element =>
                         element.Name.LocalName is "Style" or "ControlTemplate"))
            {
                if (resource.Ancestors().Any(ancestor => ancestor.Name.LocalName == "ResourceDictionary"))
                {
                    continue;
                }

                resources.Add(CreatePageResourceEntry(source, resource, resource.Parent?.Name.LocalName ?? "local"));
            }
        }

        return resources
            .Distinct()
            .ToArray();

        bool IsGlobalResourceDictionary(string path)
        {
            var normalized = path.Replace(Path.DirectorySeparatorChar, '/');
            return normalized.Contains("/Shared/Theming/Resources/", StringComparison.OrdinalIgnoreCase);
        }
    }

    private static PageLocalResourceEntry CreatePageResourceEntry(
        string source,
        XElement resource,
        string scope)
    {
        return new PageLocalResourceEntry(
            source,
            GetLine(resource),
            resource.Name.LocalName,
            GetKey(resource),
            NormalizeTargetType((string?)resource.Attribute("TargetType")),
            scope);
    }

    private static string ResolveResourcePath(string repositoryRoot, string source)
    {
        var relativeSource = source.StartsWith(WpfUiComponentPrefix, StringComparison.Ordinal)
            ? source[WpfUiComponentPrefix.Length..]
            : source.TrimStart('/');
        return Path.Combine(
            repositoryRoot,
            "src",
            "NovelSpeaker.App",
            relativeSource.Replace('/', Path.DirectorySeparatorChar));
    }

    private static bool IsStandardControl(string? targetType)
    {
        if (string.IsNullOrWhiteSpace(targetType))
        {
            return false;
        }

        return StandardWpfTargetTypes.Contains(targetType) || targetType.StartsWith("ui:", StringComparison.Ordinal);
    }

    private static string? GetKey(XElement element) =>
        (string?)element.Attribute(XNamespace.Get(XamlNamespace) + "Key");

    private static string? NormalizeTargetType(string? targetType)
    {
        if (string.IsNullOrWhiteSpace(targetType))
        {
            return null;
        }

        const string typePrefix = "{x:Type ";
        return targetType.StartsWith(typePrefix, StringComparison.Ordinal) && targetType.EndsWith('}')
            ? targetType[typePrefix.Length..^1]
            : targetType;
    }

    private static int GetLine(XElement element)
    {
        var lineInfo = (IXmlLineInfo)element;
        return lineInfo.HasLineInfo() ? lineInfo.LineNumber : 0;
    }

    private static string ToRepositoryRelativePath(string repositoryRoot, string path) =>
        Path.GetRelativePath(repositoryRoot, path).Replace(Path.DirectorySeparatorChar, '/');

    private static string ReadGitCommit(string repositoryRoot)
    {
        try
        {
            var gitDirectory = Path.Combine(repositoryRoot, ".git");
            var head = File.ReadAllText(Path.Combine(gitDirectory, "HEAD")).Trim();
            if (!head.StartsWith("ref: ", StringComparison.Ordinal))
            {
                return head;
            }

            var referencePath = Path.Combine(
                gitDirectory,
                head[5..].Replace('/', Path.DirectorySeparatorChar));
            return File.ReadAllText(referencePath).Trim();
        }
        catch (IOException)
        {
            return "unknown";
        }
        catch (UnauthorizedAccessException)
        {
            return "unknown";
        }
    }
}

internal sealed record GlobalDictionaryScanResult(
    IReadOnlyList<StyleFinding> ImplicitStyles,
    IReadOnlyList<TemplateFinding> TemplateOverrides,
    IReadOnlyList<StyleOwnershipViolation> Violations);

internal sealed record DesignTokenScanResult(
    IReadOnlyList<string> DesignTokens,
    IReadOnlyList<TokenFinding> ForbiddenDesignTokens,
    IReadOnlyList<StyleOwnershipViolation> Violations);

internal sealed record StyleOwnershipAudit(
    string SchemaVersion,
    string Task,
    string GitCommit,
    IReadOnlyList<ProviderResourceEntry> Provider,
    IReadOnlyList<GlobalDictionaryEntry> GlobalDictionaries,
    IReadOnlyList<StyleFinding> ImplicitStyles,
    IReadOnlyList<TemplateFinding> TemplateOverrides,
    IReadOnlyList<PageLocalResourceEntry> PageLocalResources,
    IReadOnlyList<string> DesignTokens,
    IReadOnlyList<TokenFinding> ForbiddenDesignTokens,
    IReadOnlyList<RuntimeFinding> ThemeRuntimeWrites,
    IReadOnlyList<StyleOwnershipViolation> Violations)
{
    public string ToJson() => JsonSerializer.Serialize(this, new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    });
}

internal sealed record ProviderResourceEntry(
    string Provider,
    string Resource,
    string Source,
    string? Theme);

internal sealed record GlobalDictionaryEntry(
    string Source,
    string Scope,
    bool Exists);

internal sealed record StyleFinding(
    string Source,
    int Line,
    string? ResourceKey,
    string? TargetType);

internal sealed record TemplateFinding(
    string Source,
    int Line,
    string? ResourceKey,
    string? TargetType,
    string Kind,
    bool Approved);

internal sealed record PageLocalResourceEntry(
    string Source,
    int Line,
    string Kind,
    string? ResourceKey,
    string? TargetType,
    string Scope);

internal sealed record TokenFinding(
    string Source,
    int Line,
    string Key);

internal sealed record RuntimeFinding(
    string Rule,
    string Source,
    int Line,
    string Detail);

internal sealed record StyleOwnershipViolation(
    string Rule,
    string Source,
    int Line,
    string Detail);
