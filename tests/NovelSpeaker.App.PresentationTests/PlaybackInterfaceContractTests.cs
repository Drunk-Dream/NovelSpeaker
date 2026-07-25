using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.DependencyInjection;
using NovelSpeaker.App;
using NovelSpeaker.Infrastructure.DependencyInjection;
using Xunit;

namespace NovelSpeaker.UnitTests.Playback;

public sealed class PlaybackInterfaceContractTests
{
    [Fact]
    public void Playback_coordinator_exposes_only_consumer_role_contracts()
    {
        var assembly = typeof(PlaybackCoordinator).Assembly;

        Assert.Null(assembly.GetType("NovelSpeaker.Application.Playback.IPlaybackCoordinator"));

        Assert.Equal(
            ["CurrentSnapshot", "SnapshotChanged"],
            GetPublicMemberNames(assembly, "IPlaybackSnapshotSource"));
        Assert.Equal(
            [
                "ChangeRuleAsync",
                "ChangeSpeedAsync",
                "CurrentSnapshot",
                "JumpToAsync",
                "JumpToChapterAsync",
                "JumpToSegmentAsync",
                "NextChapterAsync",
                "NextSegmentAsync",
                "OpenPausedAsync",
                "PauseAsync",
                "PreviousChapterAsync",
                "PreviousSegmentAsync",
                "ResumeAsync",
                "RetryCurrentSegmentAsync",
                "SnapshotChanged",
                "StartAsync",
                "StopAsync"
            ],
            GetPublicMemberNames(assembly, "IPlaybackSession"));
        Assert.Equal(
            [
                "CurrentSnapshot",
                "HandleBookDeletedAsync",
                "RefreshBookMetadataAsync",
                "SnapshotChanged"
            ],
            GetPublicMemberNames(assembly, "IPlaybackBookCommands"));
        Assert.Equal(
            ["RefreshRegexReplacementAsync"],
            GetPublicMemberNames(assembly, "IPlaybackRegexReplacementRefresher"));
    }

    [Fact]
    public void Skip_contract_and_snapshot_capability_are_removed_when_not_consumed()
    {
        var assembly = typeof(PlaybackCoordinator).Assembly;
        var coordinator = typeof(PlaybackCoordinator);
        var snapshot = typeof(PlaybackSnapshot);

        Assert.Null(coordinator.GetMethod("SkipCurrentSegmentAsync"));
        Assert.Null(snapshot.GetProperty("CanSkip"));
        Assert.Null(assembly.GetType("NovelSpeaker.Application.Playback.PlaybackRecoveryDecision")
            ?.GetProperty("CanSkip"));
    }

    [Fact]
    public async Task Playback_role_registrations_resolve_to_one_coordinator_instance()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddLogging();
        services.AddNovelSpeakerApplication();
        services.AddNovelSpeakerInfrastructure();
        services.AddNovelSpeakerDesktop();

        await using var provider = services.BuildServiceProvider();
        var coordinator = provider.GetRequiredService<PlaybackCoordinator>();

        Assert.Same(coordinator, provider.GetRequiredService<IPlaybackSnapshotSource>());
        Assert.Same(coordinator, provider.GetRequiredService<IPlaybackSession>());
        Assert.Same(coordinator, provider.GetRequiredService<IPlaybackBookCommands>());
        Assert.Same(coordinator, provider.GetRequiredService<IPlaybackRegexReplacementRefresher>());
    }

    private static string[] GetPublicMemberNames(System.Reflection.Assembly assembly, string interfaceName)
    {
        var contract = assembly.GetType($"NovelSpeaker.Application.Playback.{interfaceName}");
        Assert.NotNull(contract);

        return contract!
            .GetInterfaces()
            .Append(contract)
            .SelectMany(interfaceType => interfaceType.GetMembers(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            .Where(member => member.MemberType is System.Reflection.MemberTypes.Method or
                System.Reflection.MemberTypes.Property or
                System.Reflection.MemberTypes.Event)
            .Where(member => member is not System.Reflection.MethodBase method || !method.IsSpecialName)
            .Select(member => member.Name)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
    }
}
