using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace NovelSpeaker.App.PresentationTests.Architecture;

internal static class TextThemeArchitectureRules
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    public static IReadOnlyList<string> FindViolations(IEnumerable<SourceFileDescriptor> files)
    {
        var documents = files.Select(file => (file.RelativePath,
            Document: XDocument.Parse(file.Content, LoadOptions.SetLineInfo))).ToArray();
        var styles = documents.SelectMany(item => item.Document.Descendants())
            .Where(element => element.Name.LocalName == "Style" && element.Attribute(Xaml + "Key") is not null)
            .GroupBy(element => element.Attribute(Xaml + "Key")!.Value, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var violations = new List<string>();

        bool HasForeground(XElement style, HashSet<XElement> visiting)
        {
            if (!visiting.Add(style)) return false;
            try
            {
                var foreground = style.Elements().LastOrDefault(element =>
                    element.Name.LocalName == "Setter" && IsForeground(element.Attribute("Property")?.Value));
                if (foreground is not null)
                    return IsThemeValue(foreground.Attribute("Value")?.Value);

                var key = ResourceKey(style.Attribute("BasedOn")?.Value);
                return key is not null && styles.TryGetValue(key, out var bases) &&
                       bases.All(baseStyle => HasForeground(baseStyle, visiting));
            }
            finally
            {
                visiting.Remove(style);
            }
        }

        foreach (var (path, document) in documents)
        {
            foreach (var element in document.Descendants())
            {
                void Report(string reason) => violations.Add(
                    $"{path}:{((IXmlLineInfo)element).LineNumber}: {reason}");

                foreach (var attribute in element.Attributes().Where(attribute => IsForeground(attribute.Name.LocalName)))
                {
                    if (!IsThemeValue(attribute.Value)) Report("Foreground must use a dynamic semantic resource or explicit binding.");
                }
                if (element.Name.LocalName == "Setter" && IsForeground(element.Attribute("Property")?.Value) &&
                    !IsThemeValue(element.Attribute("Value")?.Value))
                    Report("Foreground setter must use a dynamic semantic resource or explicit binding.");
                if (element.Name.LocalName.EndsWith(".Foreground", StringComparison.Ordinal) &&
                    element.Elements().SingleOrDefault()?.Name.LocalName != "Binding")
                    Report("Foreground property elements must bind to the theme owner; use DynamicResource for semantic brushes.");

                if (element.Name.LocalName != "TextBlock") continue;
                var explicitForeground = element.Attributes().FirstOrDefault(attribute => IsForeground(attribute.Name.LocalName));
                if (explicitForeground is not null && IsThemeValue(explicitForeground.Value)) continue;
                if (element.Elements().Any(child => child.Name.LocalName == "TextBlock.Foreground" &&
                                                    child.Elements().SingleOrDefault()?.Name.LocalName == "Binding")) continue;

                var inlineStyle = element.Elements().FirstOrDefault(child => child.Name.LocalName == "TextBlock.Style")?
                    .Elements().SingleOrDefault(child => child.Name.LocalName == "Style");
                if (inlineStyle is not null && HasForeground(inlineStyle, [])) continue;

                var styleKey = ResourceKey(element.Attribute("Style")?.Value);
                if (styleKey is not null && styles.TryGetValue(styleKey, out var referencedStyles) &&
                    referencedStyles.All(style => HasForeground(style, []))) continue;

                Report("TextBlock must declare a style with a theme foreground, or an explicit theme foreground binding.");
            }
        }
        return violations;
    }

    private static bool IsForeground(string? property) =>
        property is "Foreground" || property?.EndsWith(".Foreground", StringComparison.Ordinal) == true;

    private static bool IsThemeValue(string? value) => value is not null &&
        (Regex.IsMatch(value, @"^\{DynamicResource\s+(?:ResourceKey=)?App\.Brush\.[\w.]+\s*\}$") ||
         value.StartsWith("{Binding ", StringComparison.Ordinal) ||
         value.StartsWith("{TemplateBinding ", StringComparison.Ordinal) ||
         value.StartsWith("{DynamicResource {x:Static SystemColors.", StringComparison.Ordinal) ||
         value == "Transparent");

    private static string? ResourceKey(string? value)
    {
        var match = Regex.Match(value ?? string.Empty, @"^\{(?:Static|Dynamic)Resource\s+(?:ResourceKey=)?(?<key>[\w.]+)\s*\}$");
        return match.Success ? match.Groups["key"].Value : null;
    }
}
