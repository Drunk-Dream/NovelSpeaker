using Microsoft.Extensions.DependencyInjection;
using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.App;
using NovelSpeaker.App.ViewModels;
using NovelSpeaker.Infrastructure.DependencyInjection;
using Xunit;

namespace NovelSpeaker.UnitTests.DependencyInjection;

public sealed class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddNovelSpeakerInfrastructure_registers_core_services()
    {
        Exception? capturedException = null;

        var thread = new Thread(() =>
        {
            try
            {
                var services = new ServiceCollection();
                services.AddLogging();
                services.AddNovelSpeakerInfrastructure();
                services.AddNovelSpeakerDesktop();

                var provider = services.BuildServiceProvider();
                try
                {
                    Assert.IsType<MainWindowViewModel>(provider.GetRequiredService<MainWindowViewModel>());
                    Assert.IsType<ChapterRulesViewModel>(provider.GetRequiredService<ChapterRulesViewModel>());
                    Assert.IsType<TtsRulesViewModel>(provider.GetRequiredService<TtsRulesViewModel>());
                    Assert.IsAssignableFrom<IAppDataDirectoryProvider>(provider.GetRequiredService<IAppDataDirectoryProvider>());
                    Assert.IsAssignableFrom<IDatabaseInitializer>(provider.GetRequiredService<IDatabaseInitializer>());
                    Assert.IsAssignableFrom<IChapterRuleRepository>(provider.GetRequiredService<IChapterRuleRepository>());
                    Assert.IsAssignableFrom<NovelSpeaker.Application.Speech.ITtsRuleRepository>(
                        provider.GetRequiredService<NovelSpeaker.Application.Speech.ITtsRuleRepository>());
                    Assert.IsAssignableFrom<IAppSettingsStore>(provider.GetRequiredService<IAppSettingsStore>());
                    Assert.IsAssignableFrom<ITextSegmentationOptionsProvider>(
                        provider.GetRequiredService<ITextSegmentationOptionsProvider>());
                    Assert.IsAssignableFrom<ITextSegmenter>(provider.GetRequiredService<ITextSegmenter>());
                    Assert.IsAssignableFrom<IAudioPlayer>(provider.GetRequiredService<IAudioPlayer>());
                    Assert.IsAssignableFrom<ILocalAudioPlaybackCoordinator>(provider.GetRequiredService<ILocalAudioPlaybackCoordinator>());
                    Assert.IsAssignableFrom<IPlaybackCoordinator>(provider.GetRequiredService<IPlaybackCoordinator>());
                    Assert.IsAssignableFrom<IBookPlaybackContentService>(provider.GetRequiredService<IBookPlaybackContentService>());
                    Assert.IsAssignableFrom<ISelectedTtsRuleProvider>(provider.GetRequiredService<ISelectedTtsRuleProvider>());
                    Assert.IsAssignableFrom<IPlaybackAudioProvider>(provider.GetRequiredService<IPlaybackAudioProvider>());
                    Assert.IsAssignableFrom<IAudioCache>(provider.GetRequiredService<IAudioCache>());
                    Assert.IsAssignableFrom<IAudioCacheManagementService>(provider.GetRequiredService<IAudioCacheManagementService>());
                    Assert.IsAssignableFrom<IAudioCacheProtectionRegistry>(provider.GetRequiredService<IAudioCacheProtectionRegistry>());
                    Assert.IsAssignableFrom<IPrefetchScheduler>(provider.GetRequiredService<IPrefetchScheduler>());
                    Assert.IsAssignableFrom<IReadingProgressStore>(provider.GetRequiredService<IReadingProgressStore>());
                    Assert.IsType<MainWindow>(provider.GetRequiredService<MainWindow>());
                }
                finally
                {
                    provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
            }
            catch (Exception exception)
            {
                capturedException = exception;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(capturedException);
    }
}
