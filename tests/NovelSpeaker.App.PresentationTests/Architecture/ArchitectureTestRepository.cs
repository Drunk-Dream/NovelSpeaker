using System.Xml.Linq;

namespace NovelSpeaker.App.PresentationTests.Architecture;

internal sealed class ArchitectureTestRepository
{
    private static readonly string[] ProductProjectRelativePaths =
    [
        "src/NovelSpeaker.Domain/NovelSpeaker.Domain.csproj",
        "src/NovelSpeaker.Application/NovelSpeaker.Application.csproj",
        "src/NovelSpeaker.Infrastructure/NovelSpeaker.Infrastructure.csproj",
        "src/NovelSpeaker.App/NovelSpeaker.App.csproj"
    ];

    private ArchitectureTestRepository(string rootPath)
    {
        RootPath = rootPath;
    }

    public string RootPath { get; }

    public static ArchitectureTestRepository Locate()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "NovelSpeaker.slnx")) &&
                Directory.Exists(Path.Combine(current.FullName, "src")) &&
                Directory.Exists(Path.Combine(current.FullName, "tests")))
            {
                return new ArchitectureTestRepository(current.FullName);
            }

            current = current.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate the NovelSpeaker repository from '{AppContext.BaseDirectory}'.");
    }

    public IReadOnlyList<string> ReadSolutionProjectPaths()
    {
        var solutionPath = Path.Combine(RootPath, "NovelSpeaker.slnx");
        var document = XDocument.Load(solutionPath, LoadOptions.SetLineInfo);

        return document
            .Descendants("Project")
            .Select(element => element.Attribute("Path")?.Value)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => NormalizeRelativePath(path!))
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    public ProjectDescriptor ReadProject(string relativePath)
    {
        var normalizedRelativePath = NormalizeRelativePath(relativePath);
        var projectPath = Path.Combine(RootPath, normalizedRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var projectDirectory = Path.GetDirectoryName(projectPath)
            ?? throw new InvalidOperationException($"Project path has no directory: '{projectPath}'.");
        var document = XDocument.Load(projectPath, LoadOptions.SetLineInfo);

        var projectReferences = document
            .Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(Path.Combine(projectDirectory, path!)))
            .Select(ToRepositoryRelativePath)
            .Order(StringComparer.Ordinal)
            .ToArray();

        var packageReferences = document
            .Descendants("PackageReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var frameworkReferences = document
            .Descendants("FrameworkReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var properties = document
            .Descendants()
            .Where(element => element.Parent?.Name.LocalName == "PropertyGroup" && !element.HasElements)
            .GroupBy(element => element.Name.LocalName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Last().Value.Trim(),
                StringComparer.OrdinalIgnoreCase);

        return new ProjectDescriptor(
            normalizedRelativePath,
            projectReferences,
            packageReferences,
            frameworkReferences,
            properties);
    }

    public IReadOnlyList<ProjectDescriptor> ReadProductProjects() =>
        ProductProjectRelativePaths.Select(ReadProject).ToArray();

    public IReadOnlyList<SourceFileDescriptor> ReadProductSourceFiles()
    {
        var files = new List<SourceFileDescriptor>();

        foreach (var projectPath in ProductProjectRelativePaths)
        {
            var projectDirectoryRelativePath = Path.GetDirectoryName(projectPath)!
                .Replace(Path.DirectorySeparatorChar, '/');
            var projectDirectory = Path.Combine(
                RootPath,
                projectDirectoryRelativePath.Replace('/', Path.DirectorySeparatorChar));

            foreach (var filePath in Directory.EnumerateFiles(projectDirectory, "*.cs", SearchOption.AllDirectories))
            {
                var relativeToProject = Path.GetRelativePath(projectDirectory, filePath)
                    .Replace(Path.DirectorySeparatorChar, '/');

                if (relativeToProject.StartsWith("bin/", StringComparison.Ordinal) ||
                    relativeToProject.StartsWith("obj/", StringComparison.Ordinal) ||
                    relativeToProject.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) ||
                    relativeToProject.EndsWith(".g.i.cs", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                files.Add(new SourceFileDescriptor(
                    NormalizeRelativePath(Path.Combine(projectDirectoryRelativePath, relativeToProject)),
                    projectDirectoryRelativePath,
                    File.ReadAllText(filePath)));
            }
        }

        return files.OrderBy(file => file.RelativePath, StringComparer.Ordinal).ToArray();
    }

    public string ToRepositoryRelativePath(string fullPath) =>
        NormalizeRelativePath(Path.GetRelativePath(RootPath, fullPath));

    private static string NormalizeRelativePath(string path) =>
        path.Replace('\\', '/').TrimStart('.', '/');
}

internal sealed record ProjectDescriptor(
    string RelativePath,
    IReadOnlyList<string> ProjectReferences,
    IReadOnlyList<string> PackageReferences,
    IReadOnlyList<string> FrameworkReferences,
    IReadOnlyDictionary<string, string> Properties);

internal sealed record SourceFileDescriptor(
    string RelativePath,
    string ProjectDirectoryRelativePath,
    string Content);
