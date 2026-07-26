using System.Windows;
using Xunit;

namespace NovelSpeaker.UnitTests.Architecture;

public sealed class ArchitectureRuleContractTests
{
    [Fact]
    public void ForbiddenSourceDependencyRuleRejectsAddedDependency()
    {
        var files = new[]
        {
            Source(
                "src/NovelSpeaker.Application/Playback/InvalidService.cs",
                "src/NovelSpeaker.Application",
                "using Jint; namespace NovelSpeaker.Application.Playback; public sealed class InvalidService;")
        };

        var violations = ArchitectureRules.FindForbiddenSourceDependencies(files, ["Jint"]);

        Assert.Equal(
            ["src/NovelSpeaker.Application/Playback/InvalidService.cs -> Jint"],
            violations);
    }

    [Fact]
    public void AppInfrastructureRuleRejectsDependencyOutsideCompositionBoundary()
    {
        var files = new[]
        {
            Source(
                "src/NovelSpeaker.App/ViewModels/InvalidViewModel.cs",
                "src/NovelSpeaker.App",
                "using NovelSpeaker.Infrastructure.Playback; namespace NovelSpeaker.App.ViewModels; public sealed class InvalidViewModel;")
        };

        var violations = ArchitectureRules.FindAppInfrastructureDependencies(files);

        Assert.Equal(["src/NovelSpeaker.App/ViewModels/InvalidViewModel.cs"], violations);
    }

    [Fact]
    public void SourceDependencyRulesIgnoreCommentsAndStringLiterals()
    {
        var files = new[]
        {
            Source(
                "src/NovelSpeaker.App/ViewModels/SafeViewModel.cs",
                "src/NovelSpeaker.App",
                "// NovelSpeaker.Infrastructure.Playback\nnamespace NovelSpeaker.App.ViewModels; public sealed class SafeViewModel { public string Text => \"NovelSpeaker.Infrastructure.Playback\"; }")
        };

        Assert.Empty(ArchitectureRules.FindAppInfrastructureDependencies(files));
    }

    [Fact]
    public void ServiceLocationRuleRejectsProviderUsageOutsideAllowedBoundaries()
    {
        var files = new[]
        {
            Source(
                "src/NovelSpeaker.App/Features/InvalidViewModel.cs",
                "src/NovelSpeaker.App",
                "namespace NovelSpeaker.App.Features; public sealed class InvalidViewModel(IServiceProvider services) { public object Resolve() => services.GetRequiredService<object>(); }"),
            Source(
                "src/NovelSpeaker.App/Bootstrap/AllowedComposition.cs",
                "src/NovelSpeaker.App",
                "namespace NovelSpeaker.App.Bootstrap; public sealed class AllowedComposition(IServiceProvider services);")
        };

        var violations = ArchitectureRules.FindServiceLocationDependencies(
            files,
            ["src/NovelSpeaker.App/Bootstrap/AllowedComposition.cs"]);

        Assert.Equal(["src/NovelSpeaker.App/Features/InvalidViewModel.cs"], violations);
    }

    [Fact]
    public void PublicApiRuleRejectsWpfType()
    {
        var violations = ArchitectureRules.FindForbiddenPublicApiDependencies(
            typeof(ArchitectureRuleContractTests).Assembly,
            typeof(InvalidPublicApiFixture).Namespace!);

        Assert.Contains(
            violations,
            violation => violation.EndsWith(
                $"{nameof(InvalidPublicApiFixture)}.{nameof(InvalidPublicApiFixture.WpfValue)} -> {typeof(DependencyObject).FullName}",
                StringComparison.Ordinal));
    }

    [Fact]
    public void SourceLayoutRuleRejectsNamespaceAndTypeMismatch()
    {
        var files = new[]
        {
            Source(
                "src/NovelSpeaker.Domain/Books/Book.cs",
                "src/NovelSpeaker.Domain",
                "namespace NovelSpeaker.Domain.Speech;\npublic sealed class WrongType;")
        };

        var violations = ArchitectureRules.FindSourceLayoutViolations(files);

        Assert.True(
            violations.Count == 2,
            $"Expected two violations but found:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
        Assert.Contains(violations, violation => violation.Contains("namespace", StringComparison.Ordinal));
        Assert.Contains(violations, violation => violation.Contains("public types", StringComparison.Ordinal));
    }

    private static SourceFileDescriptor Source(
        string relativePath,
        string projectDirectoryRelativePath,
        string content) =>
        new(relativePath, projectDirectoryRelativePath, content);
}

public sealed class InvalidPublicApiFixture
{
    public DependencyObject? WpfValue { get; init; }
}
