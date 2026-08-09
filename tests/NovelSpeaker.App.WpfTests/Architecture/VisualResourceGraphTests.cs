using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace NovelSpeaker.App.WpfTests.Architecture;

public sealed class VisualResourceGraphTests
{
    [Fact]
    public void Application_gallery_and_wpf_resource_graph_has_unique_closed_formal_keys()
    {
        var repositoryRoot = LocateRepositoryRoot();
        var graph = VisualResourceGraphScanner.ScanRepository(repositoryRoot);

        Assert.Contains(
            graph.XamlFiles,
            path => path.EndsWith("src/NovelSpeaker.App/Bootstrap/App.xaml", StringComparison.Ordinal));
        Assert.Contains(
            graph.XamlFiles,
            path => path.EndsWith("tools/NovelSpeaker.StyleGallery/App.xaml", StringComparison.Ordinal));
        Assert.Contains(graph.Definitions, definition => definition.Key.StartsWith("App.", StringComparison.Ordinal));
        Assert.Contains(graph.Definitions, definition => definition.Key.StartsWith("Provider.", StringComparison.Ordinal));
        Assert.Empty(graph.LegacyFixtureFindings);
        Assert.Empty(graph.ProductionFixtureFindings);
        Assert.Empty(graph.Violations);

        var duplicateKeys = graph.Definitions
            .Where(definition => definition.IsFormal)
            .GroupBy(definition => definition.Key, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .ToArray();
        Assert.Empty(duplicateKeys);

        Assert.All(
            graph.References.Where(reference => reference.Key.StartsWith("App.", StringComparison.Ordinal) ||
                                                reference.Key.StartsWith("Provider.", StringComparison.Ordinal)),
            reference => Assert.Contains(
                graph.Definitions,
                definition => definition.Key.Equals(reference.Key, StringComparison.Ordinal)));
    }

    [Fact]
    public void Application_resource_graph_preserves_provider_palette_token_style_control_theme_order()
    {
        var graph = VisualResourceGraphScanner.ScanRepository(LocateRepositoryRoot());
        var order = graph.ApplicationMergeSources;

        var themes = IndexOf(order, source => source.Equals("Wpf.Ui/ThemesDictionary", StringComparison.Ordinal));
        var controls = IndexOf(order, source => source.Equals("Wpf.Ui/ControlsDictionary", StringComparison.Ordinal));
        var provider = IndexOf(order, source => source.EndsWith("ProviderStyleBridge.xaml", StringComparison.Ordinal));
        var palette = IndexOf(order, source => source.Contains("/Palettes/", StringComparison.Ordinal));
        var tokens = IndexOf(order, source => VisualResourceGraphScanner.LayerOf(source) == ResourceLayer.Tokens);
        var styles = IndexOf(order, source => VisualResourceGraphScanner.LayerOf(source) == ResourceLayer.Styles);
        var controlThemes = IndexOf(order, source => VisualResourceGraphScanner.LayerOf(source) == ResourceLayer.ControlThemes);
        var legacy = IndexOf(order, source => VisualResourceGraphScanner.LayerOf(source) == ResourceLayer.Legacy);

        Assert.True(themes >= 0);
        Assert.True(controls >= 0);
        Assert.True(provider >= 0);
        Assert.True(themes < provider);
        Assert.True(controls < provider);
        Assert.True(palette > provider);
        Assert.True(tokens > palette);
        Assert.True(styles > tokens);
        if (controlThemes >= 0)
        {
            Assert.True(controlThemes > styles);
        }

        if (legacy >= 0)
        {
            Assert.True(legacy > Math.Max(styles, controlThemes));
        }

        Assert.Equal(
            1,
            order.Count(source => VisualResourceGraphScanner.LayerOf(source) == ResourceLayer.Legacy));
    }

    [Fact]
    public void Resource_directory_skeleton_has_one_legacy_entry_and_no_root_dictionaries()
    {
        var repositoryRoot = LocateRepositoryRoot();
        var resourcesRoot = Path.Combine(
            repositoryRoot,
            "src",
            "NovelSpeaker.App",
            "Shared",
            "Theming",
            "Resources");

        foreach (var directory in new[] { "Tokens", "Styles", "ControlThemes", "Legacy" })
        {
            Assert.True(Directory.Exists(Path.Combine(resourcesRoot, directory)), directory);
        }

        Assert.Empty(Directory.EnumerateFiles(resourcesRoot, "*.xaml", SearchOption.TopDirectoryOnly));
        Assert.Equal(
            ["LegacyStyles.xaml"],
            Directory.EnumerateFiles(
                    Path.Combine(resourcesRoot, "Legacy"),
                    "*.xaml",
                    SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Existing_page_legacy_references_are_pinned_until_page_migration()
    {
        var repositoryRoot = LocateRepositoryRoot();
        var graph = VisualResourceGraphScanner.ScanRepository(repositoryRoot);
        var legacyKeys = graph.Definitions
            .Where(definition => definition.Source.Contains(
                "/Shared/Theming/Resources/Legacy/",
                StringComparison.Ordinal))
            .Select(definition => definition.Key)
            .ToHashSet(StringComparer.Ordinal);
        var findings = VisualResourceGraphScanner.ScanPageLegacyReferences(repositoryRoot, legacyKeys);

        Assert.NotEmpty(findings);
        Assert.DoesNotContain(
            findings,
            finding => finding.Source.EndsWith(
                "/Features/Cache/CacheAndDataPage.xaml",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            findings,
            finding => finding.Source.EndsWith(
                "/Features/Diagnostics/DiagnosticsAboutPage.xaml",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            findings,
            finding => finding.Source.EndsWith(
                "/Features/Library/LibraryPage.xaml",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            findings,
            finding => finding.Source.EndsWith(
                "/Features/Library/BookCardView.xaml",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            findings,
            finding => finding.Source.EndsWith(
                "/Features/BookDetails/BookDetailsPage.xaml",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            findings,
            finding => finding.Source.EndsWith(
                "/Features/TtsRules/TtsRulesPage.xaml",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            findings,
            finding => finding.Source.EndsWith(
                "/Features/ChapterRules/ChapterRulesPage.xaml",
                StringComparison.Ordinal));
        Assert.Equal(
            "FA1E1986742C8D89E9BFFEA1548981B9520BC7702675ECACE7071073874B2C3E",
            VisualResourceGraphScanner.Fingerprint(findings));
    }

    [Fact]
    public void New_page_legacy_reference_fixture_is_detected()
    {
        var findings = VisualResourceGraphScanner.ScanPageLegacyReferenceSource(
            "fixture/NewPage.xaml",
            "<TextBlock Style=\"{StaticResource PageTitleTextBlockStyle}\" />",
            new HashSet<string>(["PageTitleTextBlockStyle"], StringComparer.Ordinal));

        Assert.Single(findings);
    }

    [Fact]
    public void Formal_resource_key_fixture_requires_app_or_provider_prefix()
    {
        var graph = VisualResourceGraphScanner.ScanDocuments(
            new ResourceGraphDocument(
                "fixture/FormalStyles.xaml",
                XDocument.Parse(
                    """
                    <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                        <Style x:Key="ButtonStyle" TargetType="Button" />
                    </ResourceDictionary>
                    """,
                    LoadOptions.SetLineInfo),
                IsFormal: true));

        Assert.Contains(graph.Violations, violation => violation.Rule == "formal-key-prefix");
    }

    [Fact]
    public void Duplicate_formal_resource_key_fixture_is_rejected()
    {
        var graph = VisualResourceGraphScanner.ScanDocuments(
            new ResourceGraphDocument(
                "fixture/First.xaml",
                XDocument.Parse(
                    """
                    <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                        <Style x:Key="App.Button.Primary" TargetType="Button" />
                    </ResourceDictionary>
                    """,
                    LoadOptions.SetLineInfo),
                IsFormal: true),
            new ResourceGraphDocument(
                "fixture/Second.xaml",
                XDocument.Parse(
                    """
                    <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                        <Style x:Key="App.Button.Primary" TargetType="Button" />
                    </ResourceDictionary>
                    """,
                    LoadOptions.SetLineInfo),
                IsFormal: true));

        Assert.Contains(graph.Violations, violation => violation.Rule == "duplicate-formal-key");
    }

    [Fact]
    public void Same_file_duplicate_formal_resource_key_fixture_is_rejected()
    {
        var graph = VisualResourceGraphScanner.ScanDocuments(
            new ResourceGraphDocument(
                "fixture/SameFile.xaml",
                XDocument.Parse(
                    """
                    <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                        <Style x:Key="App.Button.Primary" TargetType="Button" />
                        <Style x:Key="App.Button.Primary" TargetType="Button" />
                    </ResourceDictionary>
                    """,
                    LoadOptions.SetLineInfo),
                IsFormal: true));

        Assert.Contains(graph.Violations, violation => violation.Rule == "duplicate-formal-key");
    }

    [Fact]
    public void Mixed_semantic_dictionary_classifies_existing_legacy_keys_but_rejects_new_unprefixed_keys()
    {
        var graph = VisualResourceGraphScanner.ScanDocuments(
            new ResourceGraphDocument(
                "src/NovelSpeaker.App/Shared/Theming/Resources/SemanticStyles.xaml",
                XDocument.Parse(
                    """
                    <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                        <Style x:Key="PageTitleTextBlockStyle" TargetType="TextBlock" />
                        <Style x:Key="App.Typography.PageTitle" TargetType="TextBlock" />
                        <Style x:Key="NewUnprefixedStyle" TargetType="TextBlock" />
                    </ResourceDictionary>
                    """,
                    LoadOptions.SetLineInfo),
                IsFormal: true));

        Assert.DoesNotContain(
            graph.Definitions,
            definition => definition.Key == "PageTitleTextBlockStyle" && definition.IsFormal);
        Assert.Contains(graph.Violations, violation => violation.Rule == "formal-key-prefix");
    }

    [Fact]
    public void Semantic_formal_keys_participate_in_duplicate_detection()
    {
        var graph = VisualResourceGraphScanner.ScanDocuments(
            new ResourceGraphDocument(
                "src/NovelSpeaker.App/Shared/Theming/Resources/SemanticStyles.xaml",
                XDocument.Parse(
                    """
                    <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                        <Style x:Key="App.Typography.PageTitle" TargetType="TextBlock" />
                    </ResourceDictionary>
                    """,
                    LoadOptions.SetLineInfo),
                IsFormal: true),
            new ResourceGraphDocument(
                "fixture/FormalStyles.xaml",
                XDocument.Parse(
                    """
                    <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                        <Style x:Key="App.Typography.PageTitle" TargetType="TextBlock" />
                    </ResourceDictionary>
                    """,
                    LoadOptions.SetLineInfo),
                IsFormal: true));

        Assert.Contains(graph.Violations, violation => violation.Rule == "duplicate-formal-key");
    }

    [Fact]
    public void Production_control_fixture_is_rejected()
    {
        var findings = VisualResourceGraphScanner.ScanProductionControlSource(
            "src/NovelSpeaker.App/Shared/Presentation/Controls/AppPageHeader.cs",
            """
            public sealed class AppPageHeader
            {
                public AppPageHeader()
                {
                    Content = "Style Gallery fixture";
                    Value = 68;
                }
            }
            """);

        Assert.Contains(findings, finding => finding.Rule == "production-control-fixture");
    }

    [Fact]
    public void Production_control_attached_property_and_factory_fixtures_are_rejected()
    {
        var findings = VisualResourceGraphScanner.ScanProductionControlSource(
            "src/NovelSpeaker.App/Shared/Presentation/Controls/AppStatusView.cs",
            """
            public AppStatusView()
            {
                AutomationProperties.SetAutomationId(progress, "gallery-progress");
                progress.SetValue(ProgressBar.ValueProperty, 68);
                progress.SetValue(ProgressBar.IsEnabledProperty, true);
                IsChecked = true;
                Text = @"固定正则：^\s*第\s*\d+章";
                Content = CreateStatus(SymbolRegular.Warning24, "固定标题", "固定说明");
            }
            """);

        Assert.Equal(6, findings.Count);
    }

    [Fact]
    public void Production_control_dynamic_factory_fixture_is_allowed()
    {
        var findings = VisualResourceGraphScanner.ScanProductionControlSource(
            "src/NovelSpeaker.App/Shared/Presentation/Controls/AppStatusView.cs",
            """
            private static TextBlock CreateText(string text, string fontSizeKey) =>
                new() { Text = text, IsEnabled = isEnabled };
            private bool IsChecked { get; set; }
            private static TextBlock CreateDynamicText(string title) =>
                CreateText(title, "FontSizeBody");
            private static string CreateStatusText(string status) =>
                $"状态：{status}";
            private static void Apply(bool isChecked, bool isEnabled) =>
                IsChecked = isChecked;
            private static void ApplyValue(bool isEnabled) =>
                SetValue(SomeProperty, isEnabled);
            """);

        Assert.Empty(findings);
    }

    [Fact]
    public void Production_control_interpolated_and_raw_literal_fixtures_are_classified()
    {
        var fixedFindings = VisualResourceGraphScanner.ScanProductionControlSource(
            "src/NovelSpeaker.App/Shared/Presentation/Controls/AppStatusView.cs",
            "Content = $\"固定内容\";\nHeader = $@\"固定标题\";\nValue = \"\"\"固定值\"\"\";");
        var dynamicFindings = VisualResourceGraphScanner.ScanProductionControlSource(
            "src/NovelSpeaker.App/Shared/Presentation/Controls/AppStatusView.cs",
            "Content = $\"动态：{status}\";\nHeader = $@\"动态：{status}\";\nValue = $\"\"\"动态：{status}\"\"\";");
        var escapedDynamicFindings = VisualResourceGraphScanner.ScanProductionControlSource(
            "src/NovelSpeaker.App/Shared/Presentation/Controls/AppStatusView.cs",
            """
            Content = $"动态 \"标签：{status}";
            Header = $@"动态 ""标签：{status}";
            """);

        Assert.Equal(3, fixedFindings.Count);
        Assert.Empty(dynamicFindings);
        Assert.Empty(escapedDynamicFindings);
    }

    [Fact]
    public void Production_control_multiline_fixtures_are_rejected()
    {
        var findings = VisualResourceGraphScanner.ScanProductionControlSource(
            "src/NovelSpeaker.App/Shared/Presentation/Controls/AppStatusView.cs",
            """
            Content =
                "固定内容";
            var text = CreateText(
                "固定标题",
                "FontSizeBody");
            AutomationProperties.SetName(
                button,
                "固定名称");
            """);

        Assert.Equal(3, findings.Count);
        Assert.Equal([1, 3, 6], findings.Select(finding => finding.Line));
    }

    private static int IndexOf(
        IReadOnlyList<string> values,
        Func<string, bool> predicate) =>
        values.Select((value, index) => (value, index))
            .Where(pair => predicate(pair.value))
            .Select(pair => pair.index)
            .DefaultIfEmpty(-1)
            .First();

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

internal static class VisualResourceGraphScanner
{
    private const string XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";
    private const string WpfUiNamespace = "http://schemas.lepo.co/wpfui/2022/xaml";
    private const string FixedStringLiteral =
        "(?:\\$?\"(?!\")(?:(?:\\\\.)|[^\"\\\\{}\\r\\n])*\"(?!\")|\\$?@\"(?!\")(?:(?:\"\")|[^\"{}\\r\\n])*\"(?!\")|@\\$?\"(?!\")(?:(?:\"\")|[^\"{}\\r\\n])*\"(?!\")|\\$?\"{3}[^\"{}\\r\\n]*\"{3})";
    private static readonly Regex ResourceReference = new(
        @"\{(?:StaticResource|DynamicResource)\s+(?<key>[A-Za-z_][A-Za-z0-9_.]*)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ProductionFixture = new(
        $@"(?:\b(?:Text|Content|Header|ToolTip|Value|IsChecked)\s*=\s*(?:{FixedStringLiteral}|[-+]?\d+(?:\.\d+)?|\btrue\b|\bfalse\b)|\b(?:SetAutomationId|SetName|SetValue)\s*\([^,]+,\s*(?:{FixedStringLiteral}|[-+]?\d+(?:\.\d+)?|\btrue\b|\bfalse\b)|\b(?:CreateText|CreateTitle|CreateBody|this)\s*\(\s*(?:{FixedStringLiteral}|[-+]?\d+(?:\.\d+)?)|\bCreateStatus\s*\([^,]+,\s*{FixedStringLiteral})",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static ResourceGraphAudit ScanRepository(string repositoryRoot)
    {
        var scanRoots = new[]
        {
            Path.Combine(repositoryRoot, "src", "NovelSpeaker.App"),
            Path.Combine(repositoryRoot, "tools", "NovelSpeaker.StyleGallery"),
            Path.Combine(repositoryRoot, "tests", "NovelSpeaker.App.WpfTests")
        };
        var documents = scanRoots
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(root, "*.xaml", SearchOption.AllDirectories))
            .Where(path => !IsGeneratedPath(path))
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(path => new ResourceGraphDocument(
                ToRepositoryRelativePath(repositoryRoot, path),
                XDocument.Load(path, LoadOptions.SetLineInfo),
                IsFormalDictionary(path, repositoryRoot)))
            .ToArray();

        var graph = ScanDocuments(documents);
        var controlRoots = new[]
        {
            new
            {
                Path = Path.Combine(
                    repositoryRoot,
                    "src",
                    "NovelSpeaker.App",
                    "Shared",
                    "Presentation",
                    "Controls"),
                IsLegacy = false
            },
            new
            {
                Path = Path.Combine(
                    repositoryRoot,
                    "src",
                    "NovelSpeaker.App",
                    "Shared",
                    "Theming",
                    "Components"),
                IsLegacy = true
            }
        };
        var controlFindings = controlRoots
            .Where(root => Directory.Exists(root.Path))
            .SelectMany(root => Directory.EnumerateFiles(root.Path, "*.cs", SearchOption.AllDirectories)
                .Where(path => !IsGeneratedPath(path))
                .SelectMany(path => ScanProductionControlSource(
                    ToRepositoryRelativePath(repositoryRoot, path),
                    File.ReadAllText(path)))
                .Select(finding => (finding, root.IsLegacy)))
            .ToArray();
        var productionControlFindings = controlFindings
            .Where(item => !item.IsLegacy)
            .Select(item => item.finding)
            .ToArray();
        var legacyControlFindings = controlFindings
            .Where(item => item.IsLegacy)
            .Select(item => item.finding)
            .ToArray();

        return graph with
        {
            XamlFiles = documents.Select(document => document.Source).ToArray(),
            ProductionFixtureFindings = productionControlFindings,
            LegacyFixtureFindings = legacyControlFindings,
            Violations = [.. graph.Violations, .. productionControlFindings.Select(finding => new ResourceGraphViolation(
                finding.Rule,
                finding.Source,
                finding.Line,
                finding.Detail))]
        };
    }

    public static ResourceGraphAudit ScanDocuments(params ResourceGraphDocument[] documents)
    {
        var definitions = new List<ResourceKeyDefinition>();
        var references = new List<ResourceKeyReference>();
        var violations = new List<ResourceGraphViolation>();
        var applicationMergeSources = new List<string>();

        foreach (var document in documents)
        {
            foreach (var dictionary in (document.Document.Root?.DescendantsAndSelf() ?? [])
                         .Where(element => element.Name.LocalName == "ResourceDictionary"))
            {
                foreach (var resource in dictionary.Elements())
                {
                    var key = (string?)resource.Attribute(XName.Get("Key", XamlNamespace));
                    if (key is null)
                    {
                        continue;
                    }

                    var isFormal = IsFormalDefinition(document, key);
                    definitions.Add(new ResourceKeyDefinition(
                        document.Source,
                        GetLine(resource),
                        key,
                        isFormal));
                    if (isFormal && !HasFormalPrefix(key))
                    {
                        violations.Add(new ResourceGraphViolation(
                            "formal-key-prefix",
                            document.Source,
                            GetLine(resource),
                            $"Formal resource key '{key}' must use the App. or Provider. prefix."));
                    }
                }

                var source = (string?)dictionary.Attribute("Source");
                if (!string.IsNullOrWhiteSpace(source))
                {
                    applicationMergeSources.Add(source);
                }
            }

            foreach (var element in document.Document.Descendants())
            {
                foreach (var attribute in element.Attributes())
                {
                    foreach (Match match in ResourceReference.Matches(attribute.Value))
                    {
                        var key = match.Groups["key"].Value;
                        if (key.StartsWith("App.", StringComparison.Ordinal) ||
                            key.StartsWith("Provider.", StringComparison.Ordinal))
                        {
                            references.Add(new ResourceKeyReference(
                                document.Source,
                                GetLine(element),
                                key));
                        }
                    }
                }
            }
        }

        foreach (var duplicate in definitions
                     .Where(definition => definition.IsFormal)
                     .GroupBy(definition => definition.Key, StringComparer.Ordinal)
                     .Where(group => group.Count() > 1))
        {
            violations.Add(new ResourceGraphViolation(
                "duplicate-formal-key",
                duplicate.Key,
                0,
                $"Formal resource key is defined in multiple files: {string.Join(", ", duplicate.Select(definition => definition.Source).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))}."));
        }

        var definedKeys = definitions
            .Select(definition => definition.Key)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var reference in references.Where(reference => !definedKeys.Contains(reference.Key)))
        {
            violations.Add(new ResourceGraphViolation(
                "unresolved-formal-reference",
                reference.Source,
                reference.Line,
                $"Formal resource reference '{reference.Key}' has no definition in the scanned resource graph."));
        }

        var appPath = documents.FirstOrDefault(document =>
            document.Source.EndsWith("src/NovelSpeaker.App/Bootstrap/App.xaml", StringComparison.Ordinal));
        if (appPath is not null)
        {
            applicationMergeSources = appPath.Document
                .Descendants()
                .Where(element => element.Parent?.Name.LocalName.EndsWith(
                    "MergedDictionaries",
                    StringComparison.Ordinal) == true)
                .Select(element => element.Name.NamespaceName == WpfUiNamespace
                    ? $"Wpf.Ui/{element.Name.LocalName}"
                    : (string?)element.Attribute("Source"))
                .Where(source => !string.IsNullOrWhiteSpace(source))
                .Select(source => source!)
                .ToList();
        }

        return new ResourceGraphAudit(
            [],
            definitions,
            references,
            applicationMergeSources,
            [],
            [],
            violations);
    }

    public static IReadOnlyList<ProductionFixtureFinding> ScanProductionControlSource(
        string source,
        string content)
    {
        var findings = new List<ProductionFixtureFinding>();
        foreach (Match match in ProductionFixture.Matches(content))
        {
            findings.Add(new ProductionFixtureFinding(
                "production-control-fixture",
                source,
                LineAt(content, match.Index),
                "Production control source contains fixed fixture content or state."));
        }

        return findings;
    }

    public static IReadOnlyList<LegacyResourceReferenceFinding> ScanPageLegacyReferences(
        string repositoryRoot,
        IReadOnlySet<string> legacyKeys)
    {
        var appRoot = Path.Combine(repositoryRoot, "src", "NovelSpeaker.App");
        var resourcesRoot = Path.Combine(
            appRoot,
            "Shared",
            "Theming",
            "Resources") + Path.DirectorySeparatorChar;

        return Directory.EnumerateFiles(appRoot, "*.xaml", SearchOption.AllDirectories)
            .Where(path => !path.StartsWith(resourcesRoot, StringComparison.OrdinalIgnoreCase))
            .Where(path => !IsGeneratedPath(path))
            .SelectMany(path => ScanPageLegacyReferenceSource(
                ToRepositoryRelativePath(repositoryRoot, path),
                File.ReadAllText(path),
                legacyKeys))
            .OrderBy(finding => finding.Source, StringComparer.Ordinal)
            .ThenBy(finding => finding.Line)
            .ThenBy(finding => finding.Key, StringComparer.Ordinal)
            .ToArray();
    }

    public static IReadOnlyList<LegacyResourceReferenceFinding> ScanPageLegacyReferenceSource(
        string source,
        string content,
        IReadOnlySet<string> legacyKeys)
    {
        return ResourceReference.Matches(content)
            .Where(match => legacyKeys.Contains(match.Groups["key"].Value))
            .Select(match => new LegacyResourceReferenceFinding(
                source,
                LineAt(content, match.Index),
                match.Groups["key"].Value))
            .ToArray();
    }

    public static string Fingerprint(IEnumerable<LegacyResourceReferenceFinding> findings)
    {
        var canonical = string.Join(
            "\n",
            findings
                .OrderBy(finding => finding.Source, StringComparer.Ordinal)
                .ThenBy(finding => finding.Key, StringComparer.Ordinal)
                .Select(finding => $"{finding.Source}:{finding.Key}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    public static ResourceLayer LayerOf(string source)
    {
        var normalized = source.Replace('\\', '/');
        if (normalized.Contains("/Legacy/", StringComparison.OrdinalIgnoreCase) ||
            normalized.EndsWith("/LegacyStyles.xaml", StringComparison.OrdinalIgnoreCase))
        {
            return ResourceLayer.Legacy;
        }

        if (normalized.Contains("/ControlThemes/", StringComparison.OrdinalIgnoreCase))
        {
            return ResourceLayer.ControlThemes;
        }

        if (normalized.Contains("/Tokens/", StringComparison.OrdinalIgnoreCase))
        {
            return ResourceLayer.Tokens;
        }

        if (normalized.Contains("/Styles/", StringComparison.OrdinalIgnoreCase) ||
            normalized.EndsWith("/Buttons.xaml", StringComparison.OrdinalIgnoreCase) ||
            normalized.EndsWith("/Inputs.xaml", StringComparison.OrdinalIgnoreCase))
        {
            return ResourceLayer.Styles;
        }

        return ResourceLayer.Other;
    }

    private static bool IsFormalDictionary(string path, string repositoryRoot)
    {
        var relative = ToRepositoryRelativePath(repositoryRoot, path);
        var normalized = relative.Replace('\\', '/');
        if (normalized.Contains("/Shared/Theming/Provider/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!normalized.Contains("/Shared/Theming/Resources/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !normalized.Contains("/Legacy/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFormalDefinition(ResourceGraphDocument document, string key) =>
        document.IsFormal &&
        (!document.Source.EndsWith("/SemanticStyles.xaml", StringComparison.OrdinalIgnoreCase) ||
         !SemanticLegacyKeys.Contains(key));

    private static readonly HashSet<string> SemanticLegacyKeys =
    [
        "PageTitleTextBlockStyle",
        "SectionTitleTextBlockStyle",
        "PrimaryTextBlockStyle",
        "SecondaryTextBlockStyle",
        "StrongTextBlockStyle",
        "FormLabelTextBlockStyle",
        "SettingsNavigationRowTitleTextBlockStyle",
        "SettingsNavigationRowContentTemplate",
        "SettingsGroupBorderStyle",
        "SettingsRowsGroupBorderStyle",
        "SettingsRowBorderStyle",
        "SettingsLastRowBorderStyle",
        "SettingsRowTitleTextBlockStyle",
        "SettingsRowDescriptionTextBlockStyle",
        "SettingsRowValueTextBlockStyle",
        "DialogTitleTextBlockStyle",
        "StatusTextBlockStyle",
        "ErrorTextBlockStyle",
        "CardBorderStyle",
        "PopupSurfaceBorderStyle",
        "PlaybackProgressBarStyle",
        "PlaybackSliderTrackButtonStyle",
        "PlaybackSliderThumbStyle",
        "PlaybackProgressSliderStyle",
        "IconButtonControlTemplate",
        "MediaIconButtonControlTemplate",
        "BorderlessListItemButtonControlTemplate",
        "BorderlessIconButtonStyle",
        "BackIconButtonStyle",
        "SecondaryIconButtonStyle",
        "BorderlessListItemButtonStyle",
        "SettingsNavigationRowButtonStyle",
        "SelectedCardContainerStyle",
        "SelectableListItemContainerStyle",
        "SelectableCardListItemContainerStyle",
        "CurrentListItemContainerStyle",
        "DropTargetListItemContainerStyle",
        "PlaybackSpeedPillButtonStyle",
        "ToolbarValueButtonStyle",
        "MediaIconButtonStyle",
        "PrimaryPlaybackIconButtonStyle",
        "FloatingIconButtonStyle"
    ];

    private static bool HasFormalPrefix(string key) =>
        key.StartsWith("App.", StringComparison.Ordinal) ||
        key.StartsWith("Provider.", StringComparison.Ordinal);

    private static bool IsGeneratedPath(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);

    private static int GetLine(XElement element)
    {
        var lineInfo = (IXmlLineInfo)element;
        return lineInfo.HasLineInfo() ? lineInfo.LineNumber : 0;
    }

    private static int LineAt(string source, int index)
    {
        var line = 1;
        for (var offset = 0; offset < index; offset++)
        {
            if (source[offset] == '\n')
            {
                line++;
            }
        }

        return line;
    }

    private static string ToRepositoryRelativePath(string repositoryRoot, string path) =>
        Path.GetRelativePath(repositoryRoot, path).Replace(Path.DirectorySeparatorChar, '/');
}

internal sealed record ResourceGraphDocument(string Source, XDocument Document, bool IsFormal);

internal sealed record ResourceGraphAudit(
    IReadOnlyList<string> XamlFiles,
    IReadOnlyList<ResourceKeyDefinition> Definitions,
    IReadOnlyList<ResourceKeyReference> References,
    IReadOnlyList<string> ApplicationMergeSources,
    IReadOnlyList<ProductionFixtureFinding> ProductionFixtureFindings,
    IReadOnlyList<ProductionFixtureFinding> LegacyFixtureFindings,
    IReadOnlyList<ResourceGraphViolation> Violations);

internal sealed record ResourceKeyDefinition(string Source, int Line, string Key, bool IsFormal);

internal sealed record ResourceKeyReference(string Source, int Line, string Key);

internal sealed record ProductionFixtureFinding(string Rule, string Source, int Line, string Detail);

internal sealed record LegacyResourceReferenceFinding(string Source, int Line, string Key);

internal sealed record ResourceGraphViolation(string Rule, string Source, int Line, string Detail);

internal enum ResourceLayer
{
    Other,
    Tokens,
    Styles,
    ControlThemes,
    Legacy
}
