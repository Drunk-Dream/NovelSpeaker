using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Application.DependencyInjection;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.App;
using NovelSpeaker.Infrastructure.DependencyInjection;
using NovelSpeaker.Infrastructure.FileSystem;
using Xunit;

namespace NovelSpeaker.App.PresentationTests;

public sealed class PlaybackRoleRegistrationTests
{
    [Fact]
    public async Task Playback_roles_resolve_to_one_coordinator_instance()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IAppDataDirectoryProvider>(new AppDataDirectoryProvider(root));
        services.AddNovelSpeakerApplication();
        services.AddNovelSpeakerInfrastructure();
        services.AddNovelSpeakerDesktop();

        try
        {
            await using var provider = services.BuildServiceProvider();
            var coordinator = provider.GetRequiredService<PlaybackCoordinator>();

            Assert.Same(coordinator, provider.GetRequiredService<IPlaybackSnapshotSource>());
            Assert.Same(coordinator, provider.GetRequiredService<IPlaybackSession>());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
