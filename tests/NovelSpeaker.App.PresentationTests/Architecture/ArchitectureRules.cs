using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace NovelSpeaker.App.PresentationTests.Architecture;

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
                @"(?<![A-Za-z0-9_])(?:I?ServiceProvider|IKeyedServiceProvider)(?![A-Za-z0-9_])|\.\s*(?:GetRequiredService|GetRequiredKeyedService|GetRequiredKeyedServices|GetService|GetKeyedService|GetServices|GetKeyedServices)(?:\s*<|\s*\()|\.\s*BuildServiceProvider\s*\(",
                RegexOptions.CultureInvariant))
            {
                violations.Add(file.RelativePath);
            }
        }

        return violations.ToArray();
    }

    public static IReadOnlyList<string> FindFeatureDependencyCycles(
        IEnumerable<SourceFileDescriptor> files)
    {
        var graph = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var dependencyEdges = new List<(string SourcePath, string SourceFeature, string TargetNamespace, string TargetFeature)>();

        foreach (var file in files.Where(file => file.RelativePath.Contains(
                     "src/NovelSpeaker.App/Features/",
                     StringComparison.Ordinal)))
        {
            var source = PrepareSource(file);
            var sourceNamespace = file.RelativePath.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase)
                ? FindXamlClassNamespace(file.Content)
                : NamespaceDeclarationRegex().Match(source).Groups["name"].Value;
            var sourceFeature = GetFeatureName(sourceNamespace) ??
                                GetFeatureNameFromRelativePath(file.RelativePath);
            if (sourceFeature is null)
            {
                continue;
            }

            if (!graph.TryGetValue(sourceFeature, out var dependencies))
            {
                dependencies = new HashSet<string>(StringComparer.Ordinal);
                graph.Add(sourceFeature, dependencies);
            }

            foreach (var targetNamespace in FindFeatureNamespaceReferences(file))
            {
                var targetFeature = GetFeatureName(targetNamespace);
                if (targetFeature is null)
                {
                    continue;
                }

                if (!targetFeature.Equals(sourceFeature, StringComparison.Ordinal))
                {
                    dependencies.Add(targetFeature);
                    graph.TryAdd(targetFeature, []);
                    dependencyEdges.Add((file.RelativePath, sourceFeature, targetNamespace, targetFeature));
                }
            }
        }

        var index = 0;
        var indices = new Dictionary<string, int>(StringComparer.Ordinal);
        var lowLinks = new Dictionary<string, int>(StringComparer.Ordinal);
        var stack = new Stack<string>();
        var onStack = new HashSet<string>(StringComparer.Ordinal);
        var cycleComponents = new Dictionary<string, int>(StringComparer.Ordinal);

        void Visit(string feature)
        {
            indices[feature] = index;
            lowLinks[feature] = index++;
            stack.Push(feature);
            onStack.Add(feature);

            foreach (var dependency in graph[feature].Order(StringComparer.Ordinal))
            {
                if (!indices.ContainsKey(dependency))
                {
                    Visit(dependency);
                    lowLinks[feature] = Math.Min(lowLinks[feature], lowLinks[dependency]);
                }
                else if (onStack.Contains(dependency))
                {
                    lowLinks[feature] = Math.Min(lowLinks[feature], indices[dependency]);
                }
            }

            if (lowLinks[feature] != indices[feature])
            {
                return;
            }

            var component = new List<string>();
            string candidate;
            do
            {
                candidate = stack.Pop();
                onStack.Remove(candidate);
                component.Add(candidate);
            }
            while (!candidate.Equals(feature, StringComparison.Ordinal));

            if (component.Count > 1)
            {
                var componentId = cycleComponents.Count;
                foreach (var member in component)
                {
                    cycleComponents[member] = componentId;
                }
            }
        }

        foreach (var feature in graph.Keys.Order(StringComparer.Ordinal))
        {
            if (!indices.ContainsKey(feature))
            {
                Visit(feature);
            }
        }

        return dependencyEdges
            .Where(edge => cycleComponents.TryGetValue(edge.SourceFeature, out var sourceComponent) &&
                           cycleComponents.TryGetValue(edge.TargetFeature, out var targetComponent) &&
                           sourceComponent == targetComponent)
            .Select(edge => $"{edge.SourcePath} -> {edge.TargetNamespace}")
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    public static IReadOnlyList<string> FindSharedFeatureDependencies(
        IEnumerable<SourceFileDescriptor> files)
    {
        var violations = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var file in files)
        {
            foreach (var targetNamespace in FindFeatureNamespaceReferences(file))
            {
                violations.Add($"{file.RelativePath} -> {targetNamespace}");
            }
        }

        return violations.ToArray();
    }

    public static IReadOnlyList<string> FindSingletonFeaturePageOrViewModelRegistrations(
        IEnumerable<SourceFileDescriptor> files)
    {
        var violations = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var file in files.Where(file =>
                     file.ProjectDirectoryRelativePath == "src/NovelSpeaker.App"))
        {
            var source = StripCommentsAndLiterals(file.Content);
            foreach (Match match in SingletonRegistrationRegex().Matches(source))
            {
                AddOrdinarySingletonViolations(
                    violations,
                    file,
                    match.Groups["types"].Value.Split(',')
                        .Concat(FindFactoryTypes(FindGenericRegistrationArguments(source, match))),
                    source);
            }

            foreach (Match match in ServiceDescriptorSingletonRegistrationRegex().Matches(source))
            {
                AddOrdinarySingletonViolations(
                    violations,
                    file,
                    match.Groups["types"].Value.Split(',')
                        .Concat(FindFactoryTypes(FindGenericRegistrationArguments(source, match))),
                    source);
            }

            foreach (var (_, arguments) in FindNonGenericRegistrationArguments(
                         source,
                         NonGenericSingletonRegistrationRegex()))
            {
                AddOrdinarySingletonViolations(
                    violations,
                    file,
                    TypeofArgumentRegex().Matches(arguments)
                        .Select(typeMatch => typeMatch.Groups["type"].Value)
                        .Concat(FindFactoryTypes(arguments)),
                    source);
            }

            foreach (var (_, arguments) in FindNonGenericRegistrationArguments(
                         source,
                         NonGenericServiceDescriptorSingletonRegistrationRegex()))
            {
                AddOrdinarySingletonViolations(
                    violations,
                    file,
                    TypeofArgumentRegex().Matches(arguments)
                        .Select(typeMatch => typeMatch.Groups["type"].Value)
                        .Concat(FindFactoryTypes(arguments)),
                    source);
            }
        }

        return violations.ToArray();
    }

    public static IReadOnlyList<string> FindForbiddenGenericAbstractionDeclarations(
        IEnumerable<SourceFileDescriptor> files)
    {
        var forbiddenNames = new[] { "EventBus", "Messenger", "ServiceLocator" };
        var violations = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var file in files)
        {
            var source = StripCommentsAndLiterals(file.Content);
            foreach (Match match in TypeDeclarationRegex().Matches(source))
            {
                var typeName = match.Groups["name"].Value;
                if (forbiddenNames.Any(typeName.Contains))
                {
                    violations.Add($"{file.RelativePath}: {typeName}");
                }
            }

            foreach (Match match in DelegateDeclarationRegex().Matches(source))
            {
                var typeName = match.Groups["name"].Value;
                if (forbiddenNames.Any(typeName.Contains))
                {
                    violations.Add($"{file.RelativePath}: {typeName}");
                }
            }
        }

        return violations.ToArray();
    }

    public static IReadOnlyList<string> FindReadingProgressWriterDependencies(
        IEnumerable<SourceFileDescriptor> files)
    {
        var violations = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var file in files.Where(file =>
                     file.ProjectDirectoryRelativePath == "src/NovelSpeaker.App"))
        {
            var fileName = Path.GetFileName(file.RelativePath);
            if (!fileName.Contains("Page", StringComparison.Ordinal) &&
                !fileName.Contains("ViewModel", StringComparison.Ordinal))
            {
                continue;
            }

            var source = StripCommentsAndLiterals(file.Content);
            if (ReadingProgressWriterTypeRegex().Match(source) is { Success: true } match)
            {
                violations.Add($"{file.RelativePath}: {match.Value}");
            }
        }

        return violations.ToArray();
    }

    public static IReadOnlyList<string> FindLargeListClearThenAddViolations(
        IEnumerable<SourceFileDescriptor> files,
        IReadOnlyCollection<string> helperRelativePaths)
    {
        var helperPaths = helperRelativePaths.ToHashSet(StringComparer.Ordinal);
        return files
            .Where(file => helperPaths.Contains(file.RelativePath))
            .Where(file => ContainsClearThenAddLoop(StripCommentsAndLiterals(file.Content)))
            .Select(file => file.RelativePath)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    public static IReadOnlyList<string> FindConcretePlaybackCoordinatorDependencies(
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
            if (ConcretePlaybackCoordinatorRegex().IsMatch(source))
            {
                violations.Add(file.RelativePath);
            }
        }

        return violations.ToArray();
    }

    public static IReadOnlyList<string> FindPlaybackConsumerBoundaryViolations(
        IEnumerable<SourceFileDescriptor> files,
        IReadOnlyCollection<string> allowedCommandConsumerPaths,
        IReadOnlyCollection<string> allowedSnapshotConsumerPaths)
    {
        var allowedCommands = allowedCommandConsumerPaths.ToHashSet(StringComparer.Ordinal);
        var allowedSnapshots = allowedSnapshotConsumerPaths.ToHashSet(StringComparer.Ordinal);
        var violations = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var file in files.Where(file =>
                     file.ProjectDirectoryRelativePath == "src/NovelSpeaker.App"))
        {
            var source = StripCommentsAndLiterals(file.Content);
            var isCommandConsumer = source.Contains("IPlaybackSession", StringComparison.Ordinal) ||
                                    source.Contains("IPlaybackBookCommands", StringComparison.Ordinal);
            var usesPlaybackSnapshot = source.Contains("PlaybackSnapshot", StringComparison.Ordinal);
            if (!isCommandConsumer && !usesPlaybackSnapshot)
            {
                continue;
            }

            if (isCommandConsumer)
            {
                if (!allowedCommands.Contains(file.RelativePath))
                {
                    violations.Add(file.RelativePath);
                }

                continue;
            }

            if (!source.Contains("IPlaybackSnapshotSource", StringComparison.Ordinal) &&
                !allowedSnapshots.Contains(file.RelativePath))
            {
                violations.Add(file.RelativePath);
            }
        }

        return violations.ToArray();
    }

    public static IReadOnlyList<string> FindPlaybackCoordinatorRegistrations(
        IEnumerable<SourceFileDescriptor> files)
    {
        var violations = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var file in files)
        {
            var source = StripCommentsAndLiterals(file.Content);
            foreach (Match match in PlaybackCoordinatorGenericRegistrationRegex().Matches(source))
            {
                var factoryArguments = FindGenericRegistrationArguments(source, match);
                if (match.Groups["types"].Value
                        .Split(',')
                        .Any(IsPlaybackCoordinatorType) ||
                    FindFactoryTypes(factoryArguments).Any(IsPlaybackCoordinatorType))
                {
                    violations.Add($"{file.RelativePath}: {match.Groups["lifetime"].Value}");
                }
            }

            foreach (Match match in PlaybackCoordinatorServiceDescriptorGenericRegistrationRegex().Matches(source))
            {
                var factoryArguments = FindGenericRegistrationArguments(source, match);
                if (match.Groups["types"].Value
                        .Split(',')
                        .Any(IsPlaybackCoordinatorType) ||
                    FindFactoryTypes(factoryArguments).Any(IsPlaybackCoordinatorType))
                {
                    violations.Add($"{file.RelativePath}: {match.Groups["lifetime"].Value}");
                }
            }

            foreach (var (lifetime, arguments) in FindNonGenericRegistrationArguments(
                         source,
                         PlaybackCoordinatorNonGenericRegistrationRegex()))
            {
                if (TypeofArgumentRegex().Matches(arguments)
                    .Cast<Match>()
                    .Select(typeMatch => typeMatch.Groups["type"].Value)
                    .Concat(FindFactoryTypes(arguments))
                    .Any(IsPlaybackCoordinatorType))
                {
                    violations.Add($"{file.RelativePath}: {lifetime}");
                }
            }

            foreach (var (lifetime, arguments) in FindNonGenericRegistrationArguments(
                         source,
                         PlaybackCoordinatorServiceDescriptorNonGenericRegistrationRegex()))
            {
                if (TypeofArgumentRegex().Matches(arguments)
                    .Cast<Match>()
                    .Select(typeMatch => typeMatch.Groups["type"].Value)
                    .Concat(FindFactoryTypes(arguments))
                    .Any(IsPlaybackCoordinatorType))
                {
                    violations.Add($"{file.RelativePath}: {lifetime}");
                }
            }
        }

        return violations.ToArray();
    }

    public static IReadOnlyList<string> FindPlaybackSessionStateMutationViolations(
        IEnumerable<SourceFileDescriptor> files)
    {
        var allowedPaths = new HashSet<string>(StringComparer.Ordinal)
        {
            "src/NovelSpeaker.Application/Playback/PlaybackCoordinator.cs",
            "src/NovelSpeaker.Application/Playback/PlaybackCommandProcessor.cs",
            "src/NovelSpeaker.Application/Playback/PlaybackSessionState.cs",
            "src/NovelSpeaker.Application/Playback/PlaybackProgressController.cs"
        };
        var mutationPattern =
            @"\.(?:ReplaceBook|SetRule|SetPosition|SetResumePosition|SetConsecutiveSegmentFailureCount|SetSpeakSpeed|UpdateAudio|SetPositionForSave|ReplaceAudioProtection)\s*\(";

        return files
            .Where(file => file.ProjectDirectoryRelativePath == "src/NovelSpeaker.Application" &&
                           file.RelativePath.Contains("/Playback/", StringComparison.Ordinal) &&
                           !allowedPaths.Contains(file.RelativePath))
            .Where(file => Regex.IsMatch(
                StripCommentsAndLiterals(file.Content),
                mutationPattern,
                RegexOptions.CultureInvariant))
            .Select(file => file.RelativePath)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    public static IReadOnlyList<string> FindUnregisteredFireAndForgetOperations(
        IEnumerable<SourceFileDescriptor> files)
    {
        var violations = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var file in files)
        {
            var source = StripCommentsAndLiterals(file.Content);
            foreach (Match match in DirectlyDiscardedAsyncOperationRegex().Matches(source))
            {
                violations.Add(
                    $"{file.RelativePath}: directly discarded {match.Groups["operation"].Value} task");
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

    private static void AddOrdinarySingletonViolations(
        ISet<string> violations,
        SourceFileDescriptor file,
        IEnumerable<string> typeNames,
        string source)
    {
        foreach (var typeName in typeNames)
        {
            var normalizedTypeName = typeName.Trim();
            if (normalizedTypeName.StartsWith("I", StringComparison.Ordinal) &&
                normalizedTypeName.Length > 1 &&
                char.IsUpper(normalizedTypeName[1]))
            {
                continue;
            }

            if ((normalizedTypeName.EndsWith("Page", StringComparison.Ordinal) ||
                 normalizedTypeName.EndsWith("ViewModel", StringComparison.Ordinal)) &&
                IsFeatureType(file, normalizedTypeName, source))
            {
                violations.Add($"{file.RelativePath}: {normalizedTypeName}");
            }
        }
    }

    private static bool IsFeatureType(SourceFileDescriptor file, string typeName, string source)
    {
        if (file.RelativePath.Contains("src/NovelSpeaker.App/Features/", StringComparison.Ordinal) ||
            typeName.Contains("NovelSpeaker.App.Features.", StringComparison.Ordinal))
        {
            return true;
        }

        if (FeatureUsingNamespaceRegex().IsMatch(source))
        {
            return true;
        }

        return FeatureUsingAliasRegex().Matches(source)
            .Cast<Match>()
            .Any(match => typeName.Equals(match.Groups["alias"].Value, StringComparison.Ordinal) ||
                          typeName.StartsWith(match.Groups["alias"].Value + ".", StringComparison.Ordinal));
    }

    private static IEnumerable<(string Lifetime, string Arguments)> FindNonGenericRegistrationArguments(
        string source,
        Regex registrationRegex)
    {
        foreach (Match match in registrationRegex.Matches(source))
        {
            var openParenthesis = source.IndexOf('(', match.Index + match.Length - 1);
            var closeParenthesis = FindMatchingDelimiter(source, openParenthesis, '(', ')');
            if (closeParenthesis < 0)
            {
                continue;
            }

            yield return (
                match.Groups["lifetime"].Value,
                source[(openParenthesis + 1)..closeParenthesis]);
        }
    }

    private static string? FindGenericRegistrationArguments(string source, Match registrationMatch)
    {
        var openParenthesis = SkipWhitespace(source, registrationMatch.Index + registrationMatch.Length);
        if (openParenthesis >= source.Length || source[openParenthesis] != '(')
        {
            return null;
        }

        var closeParenthesis = FindMatchingDelimiter(source, openParenthesis, '(', ')');
        return closeParenthesis < 0
            ? null
            : source[(openParenthesis + 1)..closeParenthesis];
    }

    private static IEnumerable<string> FindFactoryTypes(string? source)
    {
        if (source is null)
        {
            return [];
        }

        return FactoryTypeRegex().Matches(source)
            .Cast<Match>()
            .Select(match => match.Groups["type"].Value);
    }

    private static bool IsPlaybackCoordinatorType(string typeName)
    {
        var normalizedTypeName = typeName.Trim();
        if (normalizedTypeName.StartsWith("global::", StringComparison.Ordinal))
        {
            normalizedTypeName = normalizedTypeName["global::".Length..];
        }

        return normalizedTypeName.Equals("PlaybackCoordinator", StringComparison.Ordinal) ||
               normalizedTypeName.EndsWith(".PlaybackCoordinator", StringComparison.Ordinal);
    }

    private static bool ContainsClearThenAddLoop(string source)
    {
        foreach (Match clearMatch in CollectionClearRegex().Matches(source))
        {
            var containingBlockEnd = FindContainingBlockEnd(source, clearMatch.Index);
            foreach (Match loopMatch in LoopStartRegex().Matches(source, clearMatch.Index + clearMatch.Length))
            {
                if (loopMatch.Index >= containingBlockEnd)
                {
                    break;
                }

                var openParenthesis = source.IndexOf('(', loopMatch.Index + loopMatch.Length - 1);
                var closeParenthesis = FindMatchingDelimiter(source, openParenthesis, '(', ')');
                if (closeParenthesis < 0 || closeParenthesis >= containingBlockEnd)
                {
                    continue;
                }

                var bodyStart = SkipWhitespace(source, closeParenthesis + 1);
                if (bodyStart >= containingBlockEnd)
                {
                    continue;
                }

                string body;
                if (source[bodyStart] == '{')
                {
                    var bodyEnd = FindMatchingDelimiter(source, bodyStart, '{', '}');
                    if (bodyEnd < 0 || bodyEnd >= containingBlockEnd)
                    {
                        continue;
                    }

                    body = source[(bodyStart + 1)..bodyEnd];
                }
                else
                {
                    var statementEnd = FindStatementEnd(source, bodyStart);
                    if (statementEnd < 0 || statementEnd >= containingBlockEnd)
                    {
                        continue;
                    }

                    body = source[bodyStart..(statementEnd + 1)];
                }

                if (ContainsCollectionAdd(body, clearMatch.Groups["target"].Value))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool ContainsCollectionAdd(string source, string target) =>
        Regex.IsMatch(
            source,
            $@"(?<![A-Za-z0-9_.]){Regex.Escape(target)}\s*\.\s*Add\s*\(",
            RegexOptions.CultureInvariant);

    private static int FindMatchingDelimiter(string source, int openIndex, char opening, char closing)
    {
        if (openIndex < 0 || openIndex >= source.Length || source[openIndex] != opening)
        {
            return -1;
        }

        var depth = 0;
        for (var index = openIndex; index < source.Length; index++)
        {
            if (source[index] == opening)
            {
                depth++;
            }
            else if (source[index] == closing && --depth == 0)
            {
                return index;
            }
        }

        return -1;
    }

    private static int SkipWhitespace(string source, int startIndex)
    {
        var index = startIndex;
        while (index < source.Length && char.IsWhiteSpace(source[index]))
        {
            index++;
        }

        return index;
    }

    private static int FindContainingBlockEnd(string source, int index)
    {
        var openBlocks = new Stack<int>();
        for (var currentIndex = 0; currentIndex < index; currentIndex++)
        {
            if (source[currentIndex] == '{')
            {
                openBlocks.Push(currentIndex);
            }
            else if (source[currentIndex] == '}' && openBlocks.Count > 0)
            {
                openBlocks.Pop();
            }
        }

        return openBlocks.Count == 0
            ? source.Length
            : FindMatchingDelimiter(source, openBlocks.Peek(), '{', '}');
    }

    private static int FindStatementEnd(string source, int startIndex)
    {
        var statementStart = SkipWhitespace(source, startIndex);
        if (IsKeywordAt(source, statementStart, "if"))
        {
            return FindIfStatementEnd(source, statementStart);
        }

        if (IsKeywordAt(source, statementStart, "try"))
        {
            return FindTryStatementEnd(source, statementStart);
        }

        if (IsKeywordAt(source, statementStart, "foreach") ||
            IsKeywordAt(source, statementStart, "for") ||
            IsKeywordAt(source, statementStart, "while"))
        {
            return FindLoopStatementEnd(source, statementStart);
        }

        return FindSimpleStatementEnd(source, statementStart);
    }

    private static int FindIfStatementEnd(string source, int startIndex)
    {
        var openParenthesis = source.IndexOf('(', startIndex + 2);
        var closeParenthesis = FindMatchingDelimiter(source, openParenthesis, '(', ')');
        if (closeParenthesis < 0)
        {
            return -1;
        }

        var thenStart = SkipWhitespace(source, closeParenthesis + 1);
        var thenEnd = FindEmbeddedStatementEnd(source, thenStart);
        if (thenEnd < 0)
        {
            return -1;
        }

        var elseStart = SkipWhitespace(source, thenEnd + 1);
        if (!IsKeywordAt(source, elseStart, "else"))
        {
            return thenEnd;
        }

        return FindEmbeddedStatementEnd(source, elseStart + "else".Length);
    }

    private static int FindLoopStatementEnd(string source, int startIndex)
    {
        var openParenthesis = source.IndexOf('(', startIndex);
        var closeParenthesis = FindMatchingDelimiter(source, openParenthesis, '(', ')');
        if (closeParenthesis < 0)
        {
            return -1;
        }

        return FindEmbeddedStatementEnd(source, closeParenthesis + 1);
    }

    private static int FindTryStatementEnd(string source, int startIndex)
    {
        var cursor = SkipWhitespace(source, startIndex + "try".Length);
        var statementEnd = FindEmbeddedStatementEnd(source, cursor);
        if (statementEnd < 0)
        {
            return -1;
        }

        cursor = SkipWhitespace(source, statementEnd + 1);
        while (IsKeywordAt(source, cursor, "catch"))
        {
            cursor = SkipWhitespace(source, cursor + "catch".Length);
            if (cursor < source.Length && source[cursor] == '(')
            {
                var closeParenthesis = FindMatchingDelimiter(source, cursor, '(', ')');
                if (closeParenthesis < 0)
                {
                    return -1;
                }

                cursor = SkipWhitespace(source, closeParenthesis + 1);
            }

            if (IsKeywordAt(source, cursor, "when"))
            {
                var filterOpenParenthesis = source.IndexOf('(', cursor + "when".Length);
                var filterCloseParenthesis = FindMatchingDelimiter(
                    source,
                    filterOpenParenthesis,
                    '(',
                    ')');
                if (filterCloseParenthesis < 0)
                {
                    return -1;
                }

                cursor = SkipWhitespace(source, filterCloseParenthesis + 1);
            }

            statementEnd = FindEmbeddedStatementEnd(source, cursor);
            if (statementEnd < 0)
            {
                return -1;
            }

            cursor = SkipWhitespace(source, statementEnd + 1);
        }

        if (IsKeywordAt(source, cursor, "finally"))
        {
            statementEnd = FindEmbeddedStatementEnd(source, cursor + "finally".Length);
        }

        return statementEnd;
    }

    private static int FindEmbeddedStatementEnd(string source, int startIndex)
    {
        var statementStart = SkipWhitespace(source, startIndex);
        if (statementStart >= source.Length)
        {
            return -1;
        }

        if (source[statementStart] == '{')
        {
            return FindMatchingDelimiter(source, statementStart, '{', '}');
        }

        if (IsKeywordAt(source, statementStart, "if"))
        {
            return FindIfStatementEnd(source, statementStart);
        }

        if (IsKeywordAt(source, statementStart, "try"))
        {
            return FindTryStatementEnd(source, statementStart);
        }

        if (IsKeywordAt(source, statementStart, "foreach") ||
            IsKeywordAt(source, statementStart, "for") ||
            IsKeywordAt(source, statementStart, "while"))
        {
            return FindLoopStatementEnd(source, statementStart);
        }

        return FindSimpleStatementEnd(source, statementStart);
    }

    private static bool IsKeywordAt(string source, int startIndex, string keyword) =>
        startIndex >= 0 &&
        startIndex + keyword.Length <= source.Length &&
        source.AsSpan(startIndex, keyword.Length).SequenceEqual(keyword.AsSpan()) &&
        (startIndex == 0 || !IsIdentifierCharacter(source[startIndex - 1])) &&
        (startIndex + keyword.Length == source.Length ||
         !IsIdentifierCharacter(source[startIndex + keyword.Length]));

    private static bool IsIdentifierCharacter(char character) =>
        char.IsLetterOrDigit(character) || character == '_';

    private static int FindSimpleStatementEnd(string source, int startIndex)
    {
        var parenthesisDepth = 0;
        var bracketDepth = 0;
        var braceDepth = 0;

        for (var index = startIndex; index < source.Length; index++)
        {
            switch (source[index])
            {
                case '(':
                    parenthesisDepth++;
                    break;
                case ')':
                    parenthesisDepth--;
                    break;
                case '[':
                    bracketDepth++;
                    break;
                case ']':
                    bracketDepth--;
                    break;
                case '{':
                    braceDepth++;
                    break;
                case '}':
                    braceDepth--;
                    if (parenthesisDepth == 0 && bracketDepth == 0 && braceDepth == 0)
                    {
                        return index;
                    }
                    break;
                case ';' when parenthesisDepth == 0 && bracketDepth == 0 && braceDepth == 0:
                    return index;
            }
        }

        return -1;
    }

    private static IEnumerable<string> FindFeatureNamespaceReferences(SourceFileDescriptor file)
    {
        if (!file.RelativePath.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
        {
            var source = StripCommentsAndLiterals(file.Content);
            foreach (Match match in FeatureNamespaceReferenceRegex().Matches(source))
            {
                yield return match.Groups["namespace"].Value;
            }

            yield break;
        }

        XDocument document;
        try
        {
            document = XDocument.Parse(file.Content, LoadOptions.PreserveWhitespace);
        }
        catch (XmlException)
        {
            yield break;
        }

        foreach (var attribute in document.Descendants().Attributes())
        {
            foreach (Match match in ClrNamespaceReferenceRegex().Matches(attribute.Value))
            {
                yield return match.Groups["namespace"].Value;
            }

            if (attribute.Name.LocalName.Equals("Class", StringComparison.Ordinal))
            {
                foreach (Match match in FeatureNamespaceReferenceRegex().Matches(attribute.Value))
                {
                    yield return match.Groups["namespace"].Value;
                }
            }
        }
    }

    private static string? FindXamlClassNamespace(string source)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(source, LoadOptions.PreserveWhitespace);
        }
        catch (XmlException)
        {
            return null;
        }

        foreach (var attribute in document.Descendants().Attributes().Where(attribute =>
                     attribute.Name.LocalName.Equals("Class", StringComparison.Ordinal)))
        {
            var match = FeatureNamespaceReferenceRegex().Match(attribute.Value);
            if (match.Success)
            {
                return match.Groups["namespace"].Value;
            }
        }

        return null;
    }

    private static string? GetFeatureNameFromRelativePath(string relativePath)
    {
        const string marker = "src/NovelSpeaker.App/Features/";
        if (!relativePath.StartsWith(marker, StringComparison.Ordinal))
        {
            return null;
        }

        var remainder = relativePath[marker.Length..];
        var segments = remainder.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return null;
        }

        return IsNestedFeatureRoot(segments[0]) && segments.Length > 1
            ? $"{segments[0]}.{segments[1]}"
            : segments[0];
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

    private static string PrepareSource(SourceFileDescriptor file) =>
        file.RelativePath.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase)
            ? file.Content
            : StripCommentsAndLiterals(file.Content);

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

    [GeneratedRegex(
        @"(?<![A-Za-z0-9_])(?:global::)?(?<namespace>NovelSpeaker\.App\.Features\.(?<feature>[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*))(?![A-Za-z0-9_])",
        RegexOptions.CultureInvariant)]
    private static partial Regex FeatureNamespaceReferenceRegex();

    [GeneratedRegex(
        @"\b(?:Try)?AddSingleton\s*<\s*(?<types>[^>]+)>",
        RegexOptions.CultureInvariant)]
    private static partial Regex SingletonRegistrationRegex();

    [GeneratedRegex(
        @"\bServiceDescriptor\s*\.\s*Singleton\s*<\s*(?<types>[^>]+)>",
        RegexOptions.CultureInvariant)]
    private static partial Regex ServiceDescriptorSingletonRegistrationRegex();

    [GeneratedRegex(
        @"\b(?:Try)?AddSingleton\s*\(",
        RegexOptions.CultureInvariant)]
    private static partial Regex NonGenericSingletonRegistrationRegex();

    [GeneratedRegex(
        @"\bServiceDescriptor\s*\.\s*Singleton\s*\(",
        RegexOptions.CultureInvariant)]
    private static partial Regex NonGenericServiceDescriptorSingletonRegistrationRegex();

    [GeneratedRegex(
        @"\busing\s+NovelSpeaker\.App\.Features(?:\.[A-Za-z_][A-Za-z0-9_]*)*\s*;",
        RegexOptions.CultureInvariant)]
    private static partial Regex FeatureUsingNamespaceRegex();

    [GeneratedRegex(
        @"\busing\s+(?<alias>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?:global::)?NovelSpeaker\.App\.Features(?:\.[A-Za-z_][A-Za-z0-9_]*)*\s*;",
        RegexOptions.CultureInvariant)]
    private static partial Regex FeatureUsingAliasRegex();

    [GeneratedRegex(
        @"\btypeof\s*\(\s*(?<type>[^)]+)\s*\)",
        RegexOptions.CultureInvariant)]
    private static partial Regex TypeofArgumentRegex();

    [GeneratedRegex(
        @"\b(?:new\s+|(?:GetRequiredService|GetService|GetRequiredKeyedService|GetKeyedService)\s*<\s*)(?<type>(?:global::)?[A-Za-z_][A-Za-z0-9_.]*(?:\s*<[^>]+>)?)\s*(?:\(|>)",
        RegexOptions.CultureInvariant)]
    private static partial Regex FactoryTypeRegex();

    [GeneratedRegex(
        @"\b(?:class|interface|record|struct)\s+(?:(?:class|struct)\s+)?(?<name>[A-Za-z_][A-Za-z0-9_]*)\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex TypeDeclarationRegex();

    [GeneratedRegex(
        @"\bdelegate\s+[^;{]*?\b(?<name>[A-Za-z_][A-Za-z0-9_]*)(?:\s*<[^;{>]*>)?\s*\(",
        RegexOptions.CultureInvariant)]
    private static partial Regex DelegateDeclarationRegex();

    [GeneratedRegex(
        @"\bclr-namespace\s*:\s*(?<namespace>NovelSpeaker\.App\.Features\.[A-Za-z_][A-Za-z0-9_.]*)",
        RegexOptions.CultureInvariant)]
    private static partial Regex ClrNamespaceReferenceRegex();

    [GeneratedRegex(
        """x:Class\s*=\s*["'](?<namespace>NovelSpeaker\.App\.Features\.[A-Za-z_][A-Za-z0-9_.]*)["']""",
        RegexOptions.CultureInvariant)]
    private static partial Regex XamlClassNamespaceRegex();

    [GeneratedRegex(
        @"\bI?ReadingProgress(?:Persistence)?(?:Store|Writer)\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex ReadingProgressWriterTypeRegex();

    [GeneratedRegex(
        @"(?<target>[A-Za-z_][A-Za-z0-9_.]*)\s*\.\s*Clear\s*\(\s*\)\s*;",
        RegexOptions.CultureInvariant)]
    private static partial Regex CollectionClearRegex();

    [GeneratedRegex(@"\s*(?:foreach|for|while)\s*\(", RegexOptions.CultureInvariant)]
    private static partial Regex LoopStartRegex();

    [GeneratedRegex(
        @"\b(?:Try)?Add(?<lifetime>Singleton|Transient|Scoped)\s*<\s*(?<types>[^>]+)>",
        RegexOptions.CultureInvariant)]
    private static partial Regex PlaybackCoordinatorGenericRegistrationRegex();

    [GeneratedRegex(
        @"\bServiceDescriptor\s*\.\s*(?<lifetime>Singleton|Transient|Scoped)\s*<\s*(?<types>[^>]+)>",
        RegexOptions.CultureInvariant)]
    private static partial Regex PlaybackCoordinatorServiceDescriptorGenericRegistrationRegex();

    [GeneratedRegex(
        @"\b(?:Try)?Add(?<lifetime>Singleton|Transient|Scoped)\s*\(",
        RegexOptions.CultureInvariant)]
    private static partial Regex PlaybackCoordinatorNonGenericRegistrationRegex();

    [GeneratedRegex(
        @"\bServiceDescriptor\s*\.\s*(?<lifetime>Singleton|Transient|Scoped)\s*\(",
        RegexOptions.CultureInvariant)]
    private static partial Regex PlaybackCoordinatorServiceDescriptorNonGenericRegistrationRegex();

    [GeneratedRegex(
        @"(?<![A-Za-z0-9_])(?:global::)?(?:NovelSpeaker\.Application\.Playback\.)?PlaybackCoordinator(?![A-Za-z0-9_])",
        RegexOptions.CultureInvariant)]
    private static partial Regex ConcretePlaybackCoordinatorRegex();

    [GeneratedRegex(
        @"_\s*=\s*(?:[A-Za-z_][A-Za-z0-9_]*\s*\.\s*)*(?<operation>[A-Za-z_][A-Za-z0-9_]*Async)\s*\(",
        RegexOptions.CultureInvariant)]
    private static partial Regex DirectlyDiscardedAsyncOperationRegex();

    private static string? GetFeatureName(string? namespaceName)
    {
        const string prefix = "NovelSpeaker.App.Features.";
        if (namespaceName is null ||
            !namespaceName.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var remainder = namespaceName[prefix.Length..];
        var segments = remainder.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return null;
        }

        return IsNestedFeatureRoot(segments[0]) && segments.Length > 1
            ? $"{segments[0]}.{segments[1]}"
            : segments[0];
    }

    private static bool IsNestedFeatureRoot(string feature) =>
        feature is "Books" or "Rules";
}
