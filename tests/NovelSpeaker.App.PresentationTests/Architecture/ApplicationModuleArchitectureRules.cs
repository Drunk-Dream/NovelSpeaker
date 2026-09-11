using System.Text.RegularExpressions;

namespace NovelSpeaker.App.PresentationTests.Architecture;

internal enum ApplicationModule
{
    Books,
    Speech,
    Cache,
    Playback,
    Settings,
    Desktop
}

internal sealed record ApplicationModuleDependency(
    string SourcePath,
    ApplicationModule SourceModule,
    ApplicationModule TargetModule,
    string TargetNamespace,
    string TargetType)
{
    public string Identity =>
        $"{SourcePath}|{SourceModule}|{TargetModule}|{TargetNamespace}|{TargetType}";

    public string Display =>
        $"{SourcePath}: {SourceModule} -> {TargetModule} ({TargetNamespace}.{TargetType})";
}

internal sealed record ApplicationGlobalUsing(
    string? Alias,
    bool IsStatic,
    string Target);

internal static partial class ArchitectureRules
{
    private const string ApplicationNamespacePrefix = "NovelSpeaker.Application.";

    private static readonly IReadOnlySet<ApplicationModule> ApplicationModules =
        new HashSet<ApplicationModule>
        {
            ApplicationModule.Books,
            ApplicationModule.Speech,
            ApplicationModule.Cache,
            ApplicationModule.Playback,
            ApplicationModule.Settings,
            ApplicationModule.Desktop
        };

    private static readonly IReadOnlySet<string> StableCacheIdentityTypes =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "Fingerprint",
            "TextProfileFingerprint"
        };

    private static readonly IReadOnlySet<string> PlaybackMutableTruthTypes =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "PlaybackCoordinator",
            "PlaybackCommandProcessor",
            "PlaybackSessionState"
        };

    private static readonly IReadOnlySet<string> PlaybackCommandTypes =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "IPlaybackSession",
            "IPlaybackBookCommands"
        };

    private static readonly IReadOnlySet<string> CacheMutableTruthTypes =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "ActiveCacheCoordinator",
            "CacheInvalidationCoordinator",
            "ChapterExportCoordinator",
            "IAudioCache",
            "IAudioCacheStore",
            "ICacheInvalidationCoordinator",
            "SpeechPlanRepairCoordinator"
        };

    public static IReadOnlyList<string> FindApplicationModuleDependencyViolations(
        IEnumerable<SourceFileDescriptor> files)
    {
        return FindApplicationModuleDependencies(files)
            .Where(dependency => IsForbiddenApplicationModuleDependency(dependency))
            .Select(dependency => dependency.Display)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    public static IReadOnlyList<string> FindApplicationModuleDependencyCycles(
        IEnumerable<SourceFileDescriptor> files)
    {
        var dependencies = FindApplicationModuleDependencies(files).ToArray();
        var graph = dependencies
            .GroupBy(dependency => dependency.SourceModule)
            .ToDictionary(
                group => group.Key,
                group => group.Select(dependency => dependency.TargetModule)
                    .Distinct()
                    .ToArray());

        return dependencies
            .Where(dependency => HasPath(graph, dependency.TargetModule, dependency.SourceModule))
            .Select(dependency => dependency.Display)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    public static IReadOnlyList<string> FindApplicationModuleMutableTruthViolations(
        IEnumerable<SourceFileDescriptor> files)
    {
        return FindApplicationModuleDependencies(files)
            .Where(dependency => dependency.SourceModule == ApplicationModule.Desktop)
            .Where(dependency =>
                dependency.TargetModule == ApplicationModule.Playback &&
                PlaybackMutableTruthTypes.Contains(dependency.TargetType) ||
                dependency.TargetModule == ApplicationModule.Cache &&
                CacheMutableTruthTypes.Contains(dependency.TargetType))
            .Select(dependency => dependency.Display)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    internal static IReadOnlyList<ApplicationModuleDependency> FindApplicationModuleDependencies(
        IEnumerable<SourceFileDescriptor> files)
    {
        var applicationFiles = files
            .Where(file => file.ProjectDirectoryRelativePath == "src/NovelSpeaker.Application")
            .ToArray();
        var declaredTypes = applicationFiles
            .SelectMany(GetDeclaredApplicationTypes)
            .ToArray();
        var globalUsings = applicationFiles
            .SelectMany(file => GetGlobalUsings(file.Content))
            .ToArray();
        var dependencies = new List<ApplicationModuleDependency>();
        var identities = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in applicationFiles)
        {
            var source = StripCommentsAndLiterals(file.Content);
            var namespaceMatch = NamespaceDeclarationRegex().Match(source);
            if (!namespaceMatch.Success ||
                !TryGetApplicationModule(namespaceMatch.Groups["name"].Value, out var sourceModule))
            {
                continue;
            }

            foreach (Match match in ApplicationUsingNamespaceRegex().Matches(source))
            {
                if (IsGlobalUsing(source, match.Index))
                {
                    continue;
                }

                var targetNamespace = match.Groups["namespace"].Value;
                if (!TryGetApplicationModule(targetNamespace, out var targetModule) ||
                    targetModule == sourceModule)
                {
                    continue;
                }

                AddNamespaceDependencies(
                    dependencies,
                    identities,
                    file,
                    source,
                    sourceModule,
                    targetNamespace,
                    targetModule,
                    declaredTypes);
            }

            foreach (Match match in ApplicationUsingTargetRegex().Matches(source))
            {
                if (IsGlobalUsing(source, match.Index))
                {
                    continue;
                }

                var target = match.Groups["target"].Value;
                var referencedTypes = declaredTypes
                    .Where(type => GetFullTypeName(type.Namespace, type.Name)
                        .Equals(target, StringComparison.Ordinal))
                    .ToArray();
                if (referencedTypes.Length > 0)
                {
                    foreach (var type in referencedTypes.Where(type => type.Module != sourceModule))
                    {
                        AddDependency(
                            dependencies,
                            identities,
                            new ApplicationModuleDependency(
                                file.RelativePath,
                                sourceModule,
                                type.Module,
                                type.Namespace,
                                type.Name));
                    }

                    continue;
                }

                if (TryGetApplicationModule(target, out var targetModule) &&
                    targetModule != sourceModule)
                {
                    AddNamespaceDependencies(
                        dependencies,
                        identities,
                        file,
                        source,
                        sourceModule,
                        target,
                        targetModule,
                        declaredTypes);
                }
            }

            foreach (var globalUsing in globalUsings)
            {
                AddGlobalUsingDependencies(
                    dependencies,
                    identities,
                    applicationFiles,
                    declaredTypes,
                    file,
                    source,
                    sourceModule,
                    globalUsing);
            }

            foreach (var targetNamespace in GetEnclosingApplicationNamespaces(
                         namespaceMatch.Groups["name"].Value))
            {
                if (!TryGetApplicationModule(targetNamespace, out var targetModule) ||
                    targetModule == sourceModule)
                {
                    continue;
                }

                AddNamespaceDependencies(
                    dependencies,
                    identities,
                    file,
                    source,
                    sourceModule,
                    targetNamespace,
                    targetModule,
                    declaredTypes,
                    addUnknownNamespaceEdge: false);
            }

            foreach (var type in declaredTypes.Where(type => type.Module != sourceModule))
            {
                if (!IsFullyQualifiedTypeUsed(source, type.Namespace, type.Name))
                {
                    continue;
                }

                AddDependency(
                    dependencies,
                    identities,
                    new ApplicationModuleDependency(
                        file.RelativePath,
                        sourceModule,
                        type.Module,
                        type.Namespace,
                        type.Name));
            }
        }

        return dependencies
            .OrderBy(dependency => dependency.Display, StringComparer.Ordinal)
            .ToArray();
    }

    internal static IReadOnlySet<ApplicationModule> FindApplicationModules(
        IEnumerable<SourceFileDescriptor> files)
    {
        return files
            .Where(file => file.ProjectDirectoryRelativePath == "src/NovelSpeaker.Application")
            .Select(file => NamespaceDeclarationRegex().Match(StripCommentsAndLiterals(file.Content)))
            .Where(match => match.Success)
            .Select(match => match.Groups["name"].Value)
            .Where(namespaceName => TryGetApplicationModule(namespaceName, out _))
            .Select(namespaceName =>
            {
                TryGetApplicationModule(namespaceName, out var module);
                return module;
            })
            .ToHashSet();
    }

    private static void AddNamespaceDependencies(
        ICollection<ApplicationModuleDependency> dependencies,
        ISet<string> identities,
        SourceFileDescriptor file,
        string source,
        ApplicationModule sourceModule,
        string targetNamespace,
        ApplicationModule targetModule,
        IReadOnlyList<(ApplicationModule Module, string Namespace, string Name)> declaredTypes,
        bool addUnknownNamespaceEdge = true,
        string? qualifiedNamePrefix = null)
    {
        var referencedTypes = declaredTypes
            .Where(type => type.Module == targetModule &&
                          type.Namespace.Equals(targetNamespace, StringComparison.Ordinal) &&
                          IsIdentifierUsed(
                              source,
                              qualifiedNamePrefix is null
                                  ? type.Name
                                  : $"{qualifiedNamePrefix}.{type.Name}"))
            .ToArray();
        if (referencedTypes.Length == 0 && addUnknownNamespaceEdge)
        {
            AddDependency(
                dependencies,
                identities,
                new ApplicationModuleDependency(
                    file.RelativePath,
                    sourceModule,
                    targetModule,
                    targetNamespace,
                    "*"));
            return;
        }

        foreach (var type in referencedTypes)
        {
            AddDependency(
                dependencies,
                identities,
                new ApplicationModuleDependency(
                    file.RelativePath,
                    sourceModule,
                    targetModule,
                    type.Namespace,
                    type.Name));
        }
    }

    private static void AddGlobalUsingDependencies(
        ICollection<ApplicationModuleDependency> dependencies,
        ISet<string> identities,
        IReadOnlyList<SourceFileDescriptor> applicationFiles,
        IReadOnlyList<(ApplicationModule Module, string Namespace, string Name)> declaredTypes,
        SourceFileDescriptor file,
        string source,
        ApplicationModule sourceModule,
        ApplicationGlobalUsing globalUsing)
    {
        var referencedTypes = declaredTypes
            .Where(type => GetFullTypeName(type.Namespace, type.Name)
                .Equals(globalUsing.Target, StringComparison.Ordinal))
            .ToArray();
        if (referencedTypes.Length > 0)
        {
            foreach (var type in referencedTypes.Where(type =>
                         type.Module != sourceModule &&
                         IsGlobalUsingTargetUsed(
                             applicationFiles,
                             source,
                             globalUsing,
                             type)))
            {
                AddDependency(
                    dependencies,
                    identities,
                    new ApplicationModuleDependency(
                        file.RelativePath,
                        sourceModule,
                        type.Module,
                        type.Namespace,
                        type.Name));
            }

            return;
        }

        if (!TryGetApplicationModule(globalUsing.Target, out var targetModule) ||
            targetModule == sourceModule)
        {
            return;
        }

        AddNamespaceDependencies(
            dependencies,
            identities,
            file,
            source,
            sourceModule,
            globalUsing.Target,
            targetModule,
            declaredTypes,
            addUnknownNamespaceEdge: false,
            qualifiedNamePrefix: globalUsing.Alias);
    }

    private static bool IsGlobalUsingTargetUsed(
        IReadOnlyList<SourceFileDescriptor> applicationFiles,
        string source,
        ApplicationGlobalUsing globalUsing,
        (ApplicationModule Module, string Namespace, string Name) type)
    {
        if (!globalUsing.IsStatic)
        {
            return IsIdentifierUsed(
                source,
                globalUsing.Alias is null
                    ? type.Name
                    : $"{globalUsing.Alias}");
        }

        if (IsIdentifierUsed(source, type.Name))
        {
            return true;
        }

        var staticMembers = applicationFiles
            .Where(file => GetNamespace(file).Equals(type.Namespace, StringComparison.Ordinal))
            .SelectMany(file => FindStaticMemberNames(file.Content, type.Name));
        return staticMembers.Any(member => IsIdentifierUsed(source, member));
    }

    private static IEnumerable<ApplicationGlobalUsing> GetGlobalUsings(string source)
    {
        foreach (Match match in ApplicationGlobalUsingRegex().Matches(StripCommentsAndLiterals(source)))
        {
            yield return new ApplicationGlobalUsing(
                match.Groups["alias"].Success ? match.Groups["alias"].Value : null,
                match.Groups["static"].Success,
                match.Groups["target"].Value);
        }
    }

    private static bool IsGlobalUsing(string source, int usingIndex)
    {
        var prefix = source[..usingIndex].TrimEnd();
        return prefix.EndsWith("global", StringComparison.Ordinal);
    }

    private static string GetNamespace(SourceFileDescriptor file)
    {
        var match = NamespaceDeclarationRegex().Match(StripCommentsAndLiterals(file.Content));
        return match.Success ? match.Groups["name"].Value : string.Empty;
    }

    private static IEnumerable<string> FindStaticMemberNames(
        string source,
        string typeName)
    {
        var prepared = StripCommentsAndLiterals(source);
        if (!TryFindApplicationTypeBody(
                prepared,
                typeName,
                out var typeDeclaration,
                out var typeBody))
        {
            yield break;
        }

        var members = new HashSet<string>(StringComparer.Ordinal);
        var directTypeBody = RemoveNestedTypeBodies(typeBody, members);
        foreach (Match match in ApplicationStaticMemberRegex().Matches(directTypeBody))
        {
            members.Add(match.Groups["name"].Value);
            AddFollowingFieldDeclarators(directTypeBody, match, members);
        }

        foreach (Match match in ApplicationConstMemberRegex().Matches(directTypeBody))
        {
            members.Add(match.Groups["name"].Value);
            AddFollowingFieldDeclarators(directTypeBody, match, members);
        }

        if (typeDeclaration.Groups["kind"].Value.Equals("enum", StringComparison.Ordinal))
        {
            foreach (Match match in ApplicationEnumMemberRegex().Matches(directTypeBody))
            {
                members.Add(match.Groups["name"].Value);
            }
        }

        foreach (var member in members)
        {
            yield return member;
        }
    }

    private static string RemoveNestedTypeBodies(
        string typeBody,
        ISet<string> members)
    {
        var prepared = typeBody.ToCharArray();
        foreach (Match match in ApplicationTypeDeclarationRegex().Matches(typeBody))
        {
            if (GetBraceDepth(typeBody, match.Index) != 0)
            {
                continue;
            }

            members.Add(match.Groups["name"].Value);
            var openBrace = FindTypeBodyOpeningBrace(typeBody, match, 0);
            var closeBrace = openBrace < 0
                ? -1
                : FindMatchingDelimiter(typeBody, openBrace, '{', '}');
            var end = closeBrace > openBrace
                ? closeBrace + 1
                : match.Index + match.Length;
            Array.Fill(prepared, ' ', match.Index, end - match.Index);
        }

        var directMembers = new char[prepared.Length];
        var depth = 0;
        for (var index = 0; index < prepared.Length; index++)
        {
            var character = prepared[index];
            if (character == '{')
            {
                directMembers[index] = depth == 0 ? character : ' ';
                depth++;
            }
            else if (character == '}')
            {
                depth--;
                directMembers[index] = ' ';
            }
            else
            {
                directMembers[index] = depth == 0 ? character : ' ';
            }
        }

        return new string(directMembers);
    }

    private static void AddFollowingFieldDeclarators(
        string source,
        Match declaration,
        ISet<string> members)
    {
        var terminator = declaration.Groups["terminator"].Value;
        if (terminator is not ("=" or ","))
        {
            return;
        }

        var start = declaration.Index + declaration.Length;
        var end = source.IndexOf(';', start);
        if (end < 0)
        {
            return;
        }

        var declarationTail = source[start..end];
        var parentheses = 0;
        var brackets = 0;
        var braces = 0;
        var inspectStart = terminator == "," ? 0 : -1;
        for (var index = 0; index < declarationTail.Length; index++)
        {
            var character = declarationTail[index];
            if (character == '(')
            {
                parentheses++;
                continue;
            }

            if (character == ')')
            {
                parentheses--;
                continue;
            }

            if (character == '[')
            {
                brackets++;
                continue;
            }

            if (character == ']')
            {
                brackets--;
                continue;
            }

            if (character == '{')
            {
                braces++;
                continue;
            }

            if (character == '}')
            {
                braces--;
                continue;
            }

            if (parentheses != 0 ||
                brackets != 0 ||
                braces != 0 ||
                (character != ',' && index != inspectStart))
            {
                continue;
            }

            var cursor = index == inspectStart ? index : index + 1;
            while (cursor < declarationTail.Length &&
                   char.IsWhiteSpace(declarationTail[cursor]))
            {
                cursor++;
            }

            var nameStart = cursor;
            if (cursor >= declarationTail.Length ||
                !(char.IsLetter(declarationTail[cursor]) || declarationTail[cursor] == '_'))
            {
                continue;
            }

            cursor++;
            while (cursor < declarationTail.Length &&
                   (char.IsLetterOrDigit(declarationTail[cursor]) ||
                    declarationTail[cursor] == '_'))
            {
                cursor++;
            }

            while (cursor < declarationTail.Length &&
                   char.IsWhiteSpace(declarationTail[cursor]))
            {
                cursor++;
            }

            if (cursor == declarationTail.Length ||
                declarationTail[cursor] is '=' or ',')
            {
                members.Add(declarationTail[nameStart..cursor].Trim());
            }

            inspectStart = -1;
        }
    }

    private static bool IsForbiddenApplicationModuleDependency(
        ApplicationModuleDependency dependency)
    {
        if (dependency.TargetModule == ApplicationModule.Cache &&
            dependency.SourceModule is ApplicationModule.Books or
                ApplicationModule.Speech or
                ApplicationModule.Settings)
        {
            return dependency.TargetNamespace != "NovelSpeaker.Application.Cache" ||
                !StableCacheIdentityTypes.Contains(dependency.TargetType);
        }

        return dependency.SourceModule == ApplicationModule.Cache &&
            dependency.TargetModule == ApplicationModule.Playback &&
            (PlaybackMutableTruthTypes.Contains(dependency.TargetType) ||
             PlaybackCommandTypes.Contains(dependency.TargetType));
    }

    private static bool HasPath(
        IReadOnlyDictionary<ApplicationModule, ApplicationModule[]> graph,
        ApplicationModule start,
        ApplicationModule target)
    {
        if (start == target)
        {
            return true;
        }

        var visited = new HashSet<ApplicationModule>();
        var pending = new Queue<ApplicationModule>();
        pending.Enqueue(start);

        while (pending.Count > 0)
        {
            var current = pending.Dequeue();
            if (!visited.Add(current) || !graph.TryGetValue(current, out var dependencies))
            {
                continue;
            }

            foreach (var dependency in dependencies)
            {
                if (dependency == target)
                {
                    return true;
                }

                pending.Enqueue(dependency);
            }
        }

        return false;
    }

    private static IEnumerable<(ApplicationModule Module, string Namespace, string Name)> GetDeclaredApplicationTypes(
        SourceFileDescriptor file)
    {
        var source = StripCommentsAndLiterals(file.Content);
        var namespaceMatch = NamespaceDeclarationRegex().Match(source);
        if (!namespaceMatch.Success ||
            !TryGetApplicationModule(namespaceMatch.Groups["name"].Value, out var module))
        {
            yield break;
        }

        var namespaceBodyDepth = namespaceMatch.Value.EndsWith('{') ? 1 : 0;
        var containingTypes = new List<(string Name, int CloseIndex)>();
        foreach (Match match in ApplicationTypeDeclarationRegex().Matches(source))
        {
            while (containingTypes.Count > 0 &&
                   containingTypes[^1].CloseIndex <= match.Index)
            {
                containingTypes.RemoveAt(containingTypes.Count - 1);
            }

            var depth = GetBraceDepth(source, match.Index);
            if (depth < namespaceBodyDepth)
            {
                continue;
            }

            var name = containingTypes.Count == 0
                ? match.Groups["name"].Value
                : $"{string.Join('.', containingTypes.Select(type => type.Name))}.{match.Groups["name"].Value}";
            yield return (module, namespaceMatch.Groups["name"].Value, name);

            var openBrace = FindTypeBodyOpeningBrace(source, match, depth);
            if (openBrace < 0)
            {
                continue;
            }

            var closeBrace = FindMatchingDelimiter(source, openBrace, '{', '}');
            if (closeBrace > openBrace)
            {
                containingTypes.Add((match.Groups["name"].Value, closeBrace));
            }
        }
    }

    private static bool TryFindApplicationTypeBody(
        string source,
        string typeName,
        out Match typeDeclaration,
        out string typeBody)
    {
        var containingTypes = new List<(string Name, int CloseIndex)>();
        foreach (Match match in ApplicationTypeDeclarationRegex().Matches(source))
        {
            while (containingTypes.Count > 0 &&
                   containingTypes[^1].CloseIndex <= match.Index)
            {
                containingTypes.RemoveAt(containingTypes.Count - 1);
            }

            var depth = GetBraceDepth(source, match.Index);
            var fullName = containingTypes.Count == 0
                ? match.Groups["name"].Value
                : $"{string.Join('.', containingTypes.Select(type => type.Name))}.{match.Groups["name"].Value}";
            var openBrace = FindTypeBodyOpeningBrace(source, match, depth);
            if (fullName.Equals(typeName, StringComparison.Ordinal))
            {
                if (openBrace < 0)
                {
                    break;
                }

                var closeBrace = FindMatchingDelimiter(source, openBrace, '{', '}');
                if (closeBrace <= openBrace)
                {
                    break;
                }

                typeDeclaration = match;
                typeBody = source[(openBrace + 1)..closeBrace];
                return true;
            }

            if (openBrace < 0)
            {
                continue;
            }

            var closeTypeBrace = FindMatchingDelimiter(source, openBrace, '{', '}');
            if (closeTypeBrace > openBrace)
            {
                containingTypes.Add((match.Groups["name"].Value, closeTypeBrace));
            }
        }

        typeDeclaration = null!;
        typeBody = string.Empty;
        return false;
    }

    private static int FindTypeBodyOpeningBrace(
        string source,
        Match typeDeclaration,
        int declarationDepth)
    {
        var openBrace = source.IndexOf('{', typeDeclaration.Index + typeDeclaration.Length);
        var semicolon = source.IndexOf(';', typeDeclaration.Index + typeDeclaration.Length);
        return openBrace >= 0 &&
               (semicolon < 0 || openBrace < semicolon) &&
               GetBraceDepth(source, openBrace) == declarationDepth
            ? openBrace
            : -1;
    }

    private static int GetBraceDepth(string source, int endExclusive)
    {
        var depth = 0;
        for (var index = 0; index < endExclusive; index++)
        {
            depth += source[index] switch
            {
                '{' => 1,
                '}' => -1,
                _ => 0
            };
        }

        return depth;
    }

    private static void AddDependency(
        ICollection<ApplicationModuleDependency> dependencies,
        ISet<string> identities,
        ApplicationModuleDependency dependency)
    {
        if (identities.Add(dependency.Identity))
        {
            dependencies.Add(dependency);
        }
    }

    private static bool IsIdentifierUsed(string source, string identifier) =>
        Regex.IsMatch(
            source,
            $"(?<![A-Za-z0-9_]){Regex.Escape(identifier)}(?![A-Za-z0-9_])",
            RegexOptions.CultureInvariant);

    private static string GetFullTypeName(string typeNamespace, string typeName) =>
        $"{typeNamespace}.{typeName}";

    private static bool IsFullyQualifiedTypeUsed(
        string source,
        string typeNamespace,
        string typeName) =>
        Regex.IsMatch(
            source,
            $"(?<![A-Za-z0-9_.])(?:global::)?{Regex.Escape(typeNamespace)}\\.{Regex.Escape(typeName)}(?![A-Za-z0-9_])",
            RegexOptions.CultureInvariant);

    private static bool TryGetApplicationModule(
        string namespaceName,
        out ApplicationModule module)
    {
        module = default;
        if (!namespaceName.StartsWith(ApplicationNamespacePrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var segments = namespaceName[ApplicationNamespacePrefix.Length..]
            .Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 ||
            !Enum.TryParse(segments[0], ignoreCase: false, out module) ||
            !ApplicationModules.Contains(module))
        {
            return false;
        }

        return true;
    }

    private static IEnumerable<string> GetEnclosingApplicationNamespaces(string namespaceName)
    {
        var segments = namespaceName[ApplicationNamespacePrefix.Length..]
            .Split('.', StringSplitOptions.RemoveEmptyEntries);
        for (var length = segments.Length - 1; length > 0; length--)
        {
            yield return ApplicationNamespacePrefix + string.Join('.', segments.Take(length));
        }
    }

    [GeneratedRegex(
        @"\busing\s+(?!static\b)(?:global::)?(?<namespace>NovelSpeaker\.Application\.[A-Za-z_][A-Za-z0-9_.]*)\s*;",
        RegexOptions.CultureInvariant)]
    private static partial Regex ApplicationUsingNamespaceRegex();

    [GeneratedRegex(
        @"\busing\s+(?:static\s+|[A-Za-z_][A-Za-z0-9_]*\s*=\s*)(?:global::)?(?<target>NovelSpeaker\.Application\.[A-Za-z_][A-Za-z0-9_.]*)\s*;",
        RegexOptions.CultureInvariant)]
    private static partial Regex ApplicationUsingTargetRegex();

    [GeneratedRegex(
        @"\bglobal\s+using\s+(?:(?<static>static)\s+|(?<alias>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*)?(?:global::)?(?<target>NovelSpeaker\.Application\.[A-Za-z_][A-Za-z0-9_.]*)\s*;",
        RegexOptions.CultureInvariant)]
    private static partial Regex ApplicationGlobalUsingRegex();

    [GeneratedRegex(
        @"\bstatic\s+(?:readonly\s+)?(?:[A-Za-z_][A-Za-z0-9_.<>?,\[\]]*\s+)+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*(?:<[^>{};()]+>)?\s*(?<terminator>\(|=>|\{|=|;|,)",
        RegexOptions.CultureInvariant)]
    private static partial Regex ApplicationStaticMemberRegex();

    [GeneratedRegex(
        @"\bconst\s+(?:[A-Za-z_][A-Za-z0-9_.<>?,\[\]]*\s+)+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*(?<terminator>=|;|,)",
        RegexOptions.CultureInvariant)]
    private static partial Regex ApplicationConstMemberRegex();

    [GeneratedRegex(
        @"\b(?<name>[A-Za-z_][A-Za-z0-9_]*)\b(?:\s*=\s*[^,}]+)?\s*(?:,|$)",
        RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex ApplicationEnumMemberRegex();

    [GeneratedRegex(
        @"\b(?<kind>class|interface|struct|enum|record)(?:\s+(?:class|struct))?\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex ApplicationTypeDeclarationRegex();
}
