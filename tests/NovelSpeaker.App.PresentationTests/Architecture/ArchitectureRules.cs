using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace NovelSpeaker.UnitTests.Architecture;

internal static partial class ArchitectureRules
{
    private static readonly HashSet<string> ForbiddenViewAssemblyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "PresentationCore",
        "PresentationFramework",
        "System.Xaml",
        "WindowsBase",
        "Wpf.Ui"
    };

    public static IReadOnlyList<string> FindForbiddenSourceDependencies(
        IEnumerable<SourceFileDescriptor> files,
        IReadOnlyCollection<string> forbiddenNamespacePrefixes)
    {
        var violations = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var file in files)
        {
            var source = StripCommentsAndLiterals(file.Content);

            foreach (var prefix in forbiddenNamespacePrefixes)
            {
                var pattern = $@"(?<![A-Za-z0-9_])(?:global::)?{Regex.Escape(prefix)}(?![A-Za-z0-9_])(?:\.[A-Za-z_][A-Za-z0-9_]*)*";
                if (Regex.IsMatch(source, pattern, RegexOptions.CultureInvariant))
                {
                    violations.Add($"{file.RelativePath} -> {prefix}");
                }
            }
        }

        return violations.ToArray();
    }

    public static IReadOnlyList<string> FindAppInfrastructureDependencies(
        IEnumerable<SourceFileDescriptor> appFiles)
    {
        var violations = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var file in appFiles)
        {
            var source = StripCommentsAndLiterals(file.Content);
            if (Regex.IsMatch(
                source,
                @"(?<![A-Za-z0-9_])(?:(?:global::)?NovelSpeaker\.)?Infrastructure(?:\.[A-Za-z_][A-Za-z0-9_]*)+",
                RegexOptions.CultureInvariant))
            {
                violations.Add(file.RelativePath);
            }
        }

        return violations.ToArray();
    }

    public static IReadOnlyList<string> FindServiceLocationDependencies(
        IEnumerable<SourceFileDescriptor> files,
        IReadOnlyCollection<string> allowedRelativePaths)
    {
        var allowed = allowedRelativePaths.ToHashSet(StringComparer.Ordinal);
        var violations = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var file in files)
        {
            if (allowed.Contains(file.RelativePath))
            {
                continue;
            }

            var source = StripCommentsAndLiterals(file.Content);
            if (Regex.IsMatch(
                source,
                @"(?<![A-Za-z0-9_])I?ServiceProvider(?![A-Za-z0-9_])|\.\s*GetRequiredService(?:\s*<|\s*\()|\.\s*GetService(?:\s*<|\s*\()",
                RegexOptions.CultureInvariant))
            {
                violations.Add(file.RelativePath);
            }
        }

        return violations.ToArray();
    }

    public static IReadOnlyList<string> FindSourceLayoutViolations(
        IEnumerable<SourceFileDescriptor> files)
    {
        var violations = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var file in files)
        {
            var fileName = Path.GetFileName(file.RelativePath);
            if (fileName.Equals("AssemblyInfo.cs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var source = StripCommentsAndLiterals(file.Content);
            var namespaceMatch = NamespaceDeclarationRegex().Match(source);
            var publicTypeMatches = PublicTypeDeclarationRegex().Matches(source);

            if (!namespaceMatch.Success)
            {
                violations.Add($"{file.RelativePath}: missing namespace declaration");
                continue;
            }

            if (publicTypeMatches.Count == 0)
            {
                continue;
            }

            var projectName = Path.GetFileName(file.ProjectDirectoryRelativePath);
            var relativeToProject = file.RelativePath[(file.ProjectDirectoryRelativePath.Length + 1)..];
            var relativeDirectory = Path.GetDirectoryName(relativeToProject)?.Replace('\\', '/');
            var expectedNamespace = string.IsNullOrEmpty(relativeDirectory)
                ? projectName
                : $"{projectName}.{relativeDirectory.Replace('/', '.')}";
            var actualNamespace = namespaceMatch.Groups["name"].Value;

            if (!actualNamespace.Equals(expectedNamespace, StringComparison.Ordinal))
            {
                violations.Add(
                    $"{file.RelativePath}: namespace '{actualNamespace}', expected '{expectedNamespace}'");
            }

            var expectedTypeName = fileName.EndsWith(".xaml.cs", StringComparison.OrdinalIgnoreCase)
                ? fileName[..^".xaml.cs".Length]
                : Path.GetFileNameWithoutExtension(fileName);
            var actualTypeNames = publicTypeMatches
                .Select(match => match.Groups["name"].Value)
                .ToArray();

            if (!actualTypeNames.Contains(expectedTypeName, StringComparer.Ordinal))
            {
                violations.Add(
                    $"{file.RelativePath}: public types [{string.Join(", ", actualTypeNames)}], expected primary type '{expectedTypeName}'");
            }
        }

        return violations.ToArray();
    }

    public static IReadOnlyList<string> FindForbiddenPublicApiDependencies(
        Assembly assembly,
        string namespacePrefix)
    {
        return FindForbiddenPublicApiDependencies(
            assembly,
            type => type.Namespace?.StartsWith(namespacePrefix, StringComparison.Ordinal) == true);
    }

    public static IReadOnlyList<string> FindForbiddenPublicApiDependencies(
        Assembly assembly,
        Func<Type, bool> typeFilter)
    {
        var violations = new SortedSet<string>(StringComparer.Ordinal);
        var types = assembly.GetExportedTypes()
            .Where(typeFilter);

        foreach (var type in types)
        {
            AddTypeViolation(violations, type, "base", type.BaseType);

            foreach (var implementedInterface in type.GetInterfaces())
            {
                AddTypeViolation(violations, type, "interface", implementedInterface);
            }

            foreach (var constructor in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                foreach (var parameter in constructor.GetParameters())
                {
                    AddTypeViolation(
                        violations,
                        type,
                        $".ctor({parameter.Name})",
                        parameter.ParameterType);
                }
            }

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                AddTypeViolation(violations, type, property.Name, property.PropertyType);

                foreach (var parameter in property.GetIndexParameters())
                {
                    AddTypeViolation(
                        violations,
                        type,
                        $"{property.Name}[{parameter.Name}]",
                        parameter.ParameterType);
                }
            }

            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                AddTypeViolation(violations, type, field.Name, field.FieldType);
            }

            foreach (var eventInfo in type.GetEvents(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                AddTypeViolation(violations, type, eventInfo.Name, eventInfo.EventHandlerType);
            }

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                if (method.IsSpecialName)
                {
                    continue;
                }

                AddTypeViolation(violations, type, $"{method.Name} return", method.ReturnType);
                foreach (var parameter in method.GetParameters())
                {
                    AddTypeViolation(
                        violations,
                        type,
                        $"{method.Name}({parameter.Name})",
                        parameter.ParameterType);
                }
            }
        }

        return violations.ToArray();
    }

    public static bool UsesWpf(ProjectDescriptor project)
    {
        return project.Properties.TryGetValue("UseWPF", out var useWpf) &&
            bool.TryParse(useWpf, out var enabled) &&
            enabled;
    }

    private static void AddTypeViolation(
        ISet<string> violations,
        Type declaringType,
        string member,
        Type? referencedType)
    {
        if (referencedType is null)
        {
            return;
        }

        foreach (var candidate in FlattenType(referencedType))
        {
            var assemblyName = candidate.Assembly.GetName().Name;
            if ((assemblyName is not null && ForbiddenViewAssemblyNames.Contains(assemblyName)) ||
                candidate.Namespace?.StartsWith("Wpf.Ui", StringComparison.Ordinal) == true)
            {
                violations.Add($"{declaringType.FullName}.{member} -> {candidate.FullName}");
            }
        }
    }

    private static IEnumerable<Type> FlattenType(Type type)
    {
        if (type.HasElementType && type.GetElementType() is { } elementType)
        {
            foreach (var candidate in FlattenType(elementType))
            {
                yield return candidate;
            }

            yield break;
        }

        yield return type;

        if (type.IsGenericType)
        {
            foreach (var argument in type.GetGenericArguments())
            {
                foreach (var candidate in FlattenType(argument))
                {
                    yield return candidate;
                }
            }
        }
    }

    private static string StripCommentsAndLiterals(string source)
    {
        var result = new StringBuilder(source.Length);
        var index = 0;

        while (index < source.Length)
        {
            if (source[index] == '/' && index + 1 < source.Length && source[index + 1] == '/')
            {
                ReplaceUntilLineEnd(source, result, ref index);
                continue;
            }

            if (source[index] == '/' && index + 1 < source.Length && source[index + 1] == '*')
            {
                ReplaceBlockComment(source, result, ref index);
                continue;
            }

            if (source[index] == '@' && index + 1 < source.Length && source[index + 1] == '"')
            {
                ReplaceVerbatimString(source, result, ref index);
                continue;
            }

            if (source[index] == '"')
            {
                ReplaceQuotedLiteral(source, result, ref index, '"');
                continue;
            }

            if (source[index] == '\'')
            {
                ReplaceQuotedLiteral(source, result, ref index, '\'');
                continue;
            }

            result.Append(source[index]);
            index++;
        }

        return result.ToString();
    }

    private static void ReplaceUntilLineEnd(string source, StringBuilder result, ref int index)
    {
        while (index < source.Length && source[index] != '\n')
        {
            result.Append(' ');
            index++;
        }
    }

    private static void ReplaceBlockComment(string source, StringBuilder result, ref int index)
    {
        result.Append("  ");
        index += 2;

        while (index < source.Length)
        {
            if (source[index] == '*' && index + 1 < source.Length && source[index + 1] == '/')
            {
                result.Append("  ");
                index += 2;
                return;
            }

            result.Append(source[index] == '\n' ? '\n' : ' ');
            index++;
        }
    }

    private static void ReplaceVerbatimString(string source, StringBuilder result, ref int index)
    {
        result.Append("  ");
        index += 2;

        while (index < source.Length)
        {
            if (source[index] == '"')
            {
                if (index + 1 < source.Length && source[index + 1] == '"')
                {
                    result.Append("  ");
                    index += 2;
                    continue;
                }

                result.Append(' ');
                index++;
                return;
            }

            result.Append(source[index] == '\n' ? '\n' : ' ');
            index++;
        }
    }

    private static void ReplaceQuotedLiteral(
        string source,
        StringBuilder result,
        ref int index,
        char delimiter)
    {
        result.Append(' ');
        index++;

        while (index < source.Length)
        {
            if (source[index] == '\\' && index + 1 < source.Length)
            {
                result.Append("  ");
                index += 2;
                continue;
            }

            var current = source[index];
            result.Append(current == '\n' ? '\n' : ' ');
            index++;

            if (current == delimiter)
            {
                return;
            }
        }
    }

    [GeneratedRegex(@"\bnamespace\s+(?<name>[A-Za-z_][A-Za-z0-9_.]*)\s*[;{]", RegexOptions.CultureInvariant)]
    private static partial Regex NamespaceDeclarationRegex();

    [GeneratedRegex(
        @"(?m)^\s*public\s+(?:(?:sealed|abstract|static|partial|readonly|ref)\s+)*(?:class|struct|interface|enum|record(?:\s+(?:class|struct))?)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)",
        RegexOptions.CultureInvariant)]
    private static partial Regex PublicTypeDeclarationRegex();
}
