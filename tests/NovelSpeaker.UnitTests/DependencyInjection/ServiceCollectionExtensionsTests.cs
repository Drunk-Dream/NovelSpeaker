using Microsoft.Extensions.DependencyInjection;
using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Application.Speech;
using NovelSpeaker.App;
using NovelSpeaker.App.Dialogs;
using NovelSpeaker.App.Feedback;
using NovelSpeaker.App.Library;
using NovelSpeaker.App.Pages;
using NovelSpeaker.App.Player;
using NovelSpeaker.App.Shell;
using NovelSpeaker.App.Theming;
using NovelSpeaker.App.ViewModels;
using Wpf.Ui;
using Wpf.Ui.Abstractions;
using Xunit;

namespace NovelSpeaker.UnitTests.DependencyInjection;

public sealed class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddNovelSpeakerInfrastructure_registers_core_services()
    {
        WpfTestHost.RunInSta(() =>
        {
            var provider = WpfTestHost.BuildServiceProvider();
            try
            {
                Assert.IsType<MainWindowViewModel>(provider.GetRequiredService<MainWindowViewModel>());
                Assert.IsAssignableFrom<INavigationService>(provider.GetRequiredService<INavigationService>());
                Assert.IsAssignableFrom<INavigationViewPageProvider>(provider.GetRequiredService<INavigationViewPageProvider>());
                Assert.IsAssignableFrom<IContentDialogService>(provider.GetRequiredService<IContentDialogService>());
                Assert.IsAssignableFrom<ISnackbarService>(provider.GetRequiredService<ISnackbarService>());
                Assert.IsAssignableFrom<IAppDialogService>(provider.GetRequiredService<IAppDialogService>());
                Assert.IsAssignableFrom<IAppNotificationService>(provider.GetRequiredService<IAppNotificationService>());
                Assert.IsAssignableFrom<IExceptionProjector>(provider.GetRequiredService<IExceptionProjector>());
                Assert.IsAssignableFrom<IAppFeedbackService>(provider.GetRequiredService<IAppFeedbackService>());
                Assert.IsAssignableFrom<IImportBookDialogService>(provider.GetRequiredService<IImportBookDialogService>());
                Assert.IsAssignableFrom<IBookDeleteDialogService>(provider.GetRequiredService<IBookDeleteDialogService>());
                Assert.IsAssignableFrom<IShellLayoutController>(provider.GetRequiredService<IShellLayoutController>());
                Assert.IsAssignableFrom<IPlayerLayoutController>(provider.GetRequiredService<IPlayerLayoutController>());
                Assert.IsAssignableFrom<IBookCoverGenerator>(provider.GetRequiredService<IBookCoverGenerator>());
                Assert.IsType<LibraryScrollState>(provider.GetRequiredService<LibraryScrollState>());
                Assert.IsAssignableFrom<IBookCatalogInvalidationState>(provider.GetRequiredService<IBookCatalogInvalidationState>());
                Assert.IsAssignableFrom<IThemePreferenceService>(provider.GetRequiredService<IThemePreferenceService>());
                Assert.IsType<BookDetailsViewModel>(provider.GetRequiredService<BookDetailsViewModel>());
                Assert.IsType<ChapterRulesViewModel>(provider.GetRequiredService<ChapterRulesViewModel>());
                Assert.IsType<TtsRulesViewModel>(provider.GetRequiredService<TtsRulesViewModel>());
                Assert.IsType<LibraryPage>(provider.GetRequiredService<LibraryPage>());
                Assert.IsType<SettingsPage>(provider.GetRequiredService<SettingsPage>());
                Assert.IsType<PlayerPage>(provider.GetRequiredService<PlayerPage>());
                Assert.IsType<TtsRulesPage>(provider.GetRequiredService<TtsRulesPage>());
                Assert.IsType<ChapterRulesPage>(provider.GetRequiredService<ChapterRulesPage>());
                Assert.IsType<BookDetailsPage>(provider.GetRequiredService<BookDetailsPage>());
                Assert.IsType<CacheManagementPage>(provider.GetRequiredService<CacheManagementPage>());
                Assert.IsAssignableFrom<IAppDataDirectoryProvider>(provider.GetRequiredService<IAppDataDirectoryProvider>());
                Assert.IsAssignableFrom<IDatabaseInitializer>(provider.GetRequiredService<IDatabaseInitializer>());
                Assert.IsAssignableFrom<IChapterRuleRepository>(provider.GetRequiredService<IChapterRuleRepository>());
                Assert.IsAssignableFrom<NovelSpeaker.Application.Speech.ITtsRuleRepository>(
                    provider.GetRequiredService<NovelSpeaker.Application.Speech.ITtsRuleRepository>());
                Assert.IsAssignableFrom<IAppSettingsStore>(provider.GetRequiredService<IAppSettingsStore>());
                Assert.IsAssignableFrom<IBookFileNameTemplateProvider>(
                    provider.GetRequiredService<IBookFileNameTemplateProvider>());
                Assert.IsAssignableFrom<ITextSegmentationOptionsProvider>(
                    provider.GetRequiredService<ITextSegmentationOptionsProvider>());
                Assert.IsAssignableFrom<ITextSegmenter>(provider.GetRequiredService<ITextSegmenter>());
                Assert.IsAssignableFrom<IBookContentReader>(provider.GetRequiredService<IBookContentReader>());
                Assert.IsAssignableFrom<IAudioPlayer>(provider.GetRequiredService<IAudioPlayer>());
                Assert.IsAssignableFrom<ILocalAudioPlaybackCoordinator>(provider.GetRequiredService<ILocalAudioPlaybackCoordinator>());
                Assert.IsAssignableFrom<IPlaybackCoordinator>(provider.GetRequiredService<IPlaybackCoordinator>());
                Assert.IsAssignableFrom<IBookPlaybackContentService>(provider.GetRequiredService<IBookPlaybackContentService>());
                Assert.IsAssignableFrom<ISelectedTtsRuleProvider>(provider.GetRequiredService<ISelectedTtsRuleProvider>());
                Assert.IsAssignableFrom<IPlaybackAudioProvider>(provider.GetRequiredService<IPlaybackAudioProvider>());
                Assert.IsAssignableFrom<ITtsRateLimiter>(provider.GetRequiredService<ITtsRateLimiter>());
                Assert.IsAssignableFrom<IAudioCache>(provider.GetRequiredService<IAudioCache>());
                Assert.IsAssignableFrom<IAudioCacheManagementService>(provider.GetRequiredService<IAudioCacheManagementService>());
                Assert.IsAssignableFrom<IAudioCacheProtectionRegistry>(provider.GetRequiredService<IAudioCacheProtectionRegistry>());
                Assert.IsAssignableFrom<IPrefetchScheduler>(provider.GetRequiredService<IPrefetchScheduler>());
                Assert.IsAssignableFrom<IReadingProgressStore>(provider.GetRequiredService<IReadingProgressStore>());
                Assert.IsAssignableFrom<TimeProvider>(provider.GetRequiredService<TimeProvider>());
                Assert.IsType<MainWindow>(provider.GetRequiredService<MainWindow>());
            }
            finally
            {
                provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
    }
}
