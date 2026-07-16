using Microsoft.Extensions.DependencyInjection;
using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.DependencyInjection;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Application.Speech;
using NovelSpeaker.App;
using NovelSpeaker.App.Dialogs;
using NovelSpeaker.App.Diagnostics;
using NovelSpeaker.App.Feedback;
using NovelSpeaker.App.Library;
using NovelSpeaker.App.Navigation;
using NovelSpeaker.App.Pages;
using NovelSpeaker.App.Player;
using NovelSpeaker.App.Shell;
using NovelSpeaker.App.Theming;
using NovelSpeaker.App.ViewModels;
using NovelSpeaker.Infrastructure.DependencyInjection;
using Wpf.Ui;
using Wpf.Ui.Abstractions;
using Xunit;

namespace NovelSpeaker.UnitTests.DependencyInjection;

public sealed class ServiceCollectionExtensionsTests
{
    [Fact]
    public void Composition_root_registers_and_validates_core_services()
    {
        WpfTestHost.RunInSta(() =>
        {
            var provider = WpfTestHost.BuildServiceProvider(validate: true);
            try
            {
                Assert.IsType<MainWindowViewModel>(provider.GetRequiredService<MainWindowViewModel>());
                Assert.IsAssignableFrom<INavigationService>(provider.GetRequiredService<INavigationService>());
                Assert.IsAssignableFrom<INavigationGuardService>(provider.GetRequiredService<INavigationGuardService>());
                Assert.IsAssignableFrom<IGuardedNavigationService>(provider.GetRequiredService<IGuardedNavigationService>());
                Assert.IsAssignableFrom<INavigationViewPageProvider>(provider.GetRequiredService<INavigationViewPageProvider>());
                Assert.IsAssignableFrom<IContentDialogService>(provider.GetRequiredService<IContentDialogService>());
                Assert.IsAssignableFrom<ISnackbarService>(provider.GetRequiredService<ISnackbarService>());
                Assert.IsAssignableFrom<IAppDialogService>(provider.GetRequiredService<IAppDialogService>());
                Assert.IsAssignableFrom<IAppNotificationService>(provider.GetRequiredService<IAppNotificationService>());
                Assert.IsAssignableFrom<IExceptionProjector>(provider.GetRequiredService<IExceptionProjector>());
                Assert.IsAssignableFrom<IAppFeedbackService>(provider.GetRequiredService<IAppFeedbackService>());
                Assert.IsAssignableFrom<IAppDiagnosticsService>(provider.GetRequiredService<IAppDiagnosticsService>());
                Assert.IsAssignableFrom<IEncodingSelectionDialogService>(provider.GetRequiredService<IEncodingSelectionDialogService>());
                Assert.IsAssignableFrom<IImportProgressDialogService>(provider.GetRequiredService<IImportProgressDialogService>());
                Assert.IsAssignableFrom<IBookDeleteDialogService>(provider.GetRequiredService<IBookDeleteDialogService>());
                Assert.IsAssignableFrom<IShellLayoutController>(provider.GetRequiredService<IShellLayoutController>());
                Assert.IsAssignableFrom<IPlayerAutoScrollCoordinator>(provider.GetRequiredService<IPlayerAutoScrollCoordinator>());
                Assert.IsAssignableFrom<IBookCoverGenerator>(provider.GetRequiredService<IBookCoverGenerator>());
                Assert.IsType<LibraryScrollState>(provider.GetRequiredService<LibraryScrollState>());
                Assert.IsAssignableFrom<ILibraryImportCoordinator>(provider.GetRequiredService<ILibraryImportCoordinator>());
                Assert.IsAssignableFrom<IBookCatalogInvalidationState>(provider.GetRequiredService<IBookCatalogInvalidationState>());
                Assert.IsAssignableFrom<IThemePreferenceService>(provider.GetRequiredService<IThemePreferenceService>());
                Assert.IsType<BookDetailsViewModel>(provider.GetRequiredService<BookDetailsViewModel>());
                Assert.IsType<CacheAndDataViewModel>(provider.GetRequiredService<CacheAndDataViewModel>());
                Assert.IsType<CacheManagementViewModel>(provider.GetRequiredService<CacheManagementViewModel>());
                Assert.IsType<ChapterRulesViewModel>(provider.GetRequiredService<ChapterRulesViewModel>());
                Assert.IsType<TtsRulesViewModel>(provider.GetRequiredService<TtsRulesViewModel>());
                Assert.IsType<LibraryPage>(provider.GetRequiredService<LibraryPage>());
                Assert.IsType<SettingsPage>(provider.GetRequiredService<SettingsPage>());
                Assert.IsType<CacheAndDataPage>(provider.GetRequiredService<CacheAndDataPage>());
                Assert.IsType<PlaybackSettingsPage>(provider.GetRequiredService<PlaybackSettingsPage>());
                Assert.IsType<ImportTextSettingsPage>(provider.GetRequiredService<ImportTextSettingsPage>());
                Assert.IsType<AppearanceSettingsPage>(provider.GetRequiredService<AppearanceSettingsPage>());
                Assert.IsType<DiagnosticsAboutPage>(provider.GetRequiredService<DiagnosticsAboutPage>());
                Assert.IsType<PlayerPage>(provider.GetRequiredService<PlayerPage>());
                Assert.IsType<TtsRulesPage>(provider.GetRequiredService<TtsRulesPage>());
                Assert.IsType<ChapterRulesPage>(provider.GetRequiredService<ChapterRulesPage>());
                Assert.IsType<BookDetailsPage>(provider.GetRequiredService<BookDetailsPage>());
                Assert.IsType<CacheManagementPage>(provider.GetRequiredService<CacheManagementPage>());
                Assert.IsAssignableFrom<IAppDataDirectoryProvider>(provider.GetRequiredService<IAppDataDirectoryProvider>());
                Assert.IsAssignableFrom<IDatabaseInitializer>(provider.GetRequiredService<IDatabaseInitializer>());
                Assert.IsAssignableFrom<IChapterRuleRepository>(provider.GetRequiredService<IChapterRuleRepository>());
                Assert.IsAssignableFrom<IChapterRuleWorkspaceService>(provider.GetRequiredService<IChapterRuleWorkspaceService>());
                Assert.IsAssignableFrom<NovelSpeaker.Application.Speech.ITtsRuleRepository>(
                    provider.GetRequiredService<NovelSpeaker.Application.Speech.ITtsRuleRepository>());
                Assert.IsAssignableFrom<IDirectBookImportService>(provider.GetRequiredService<IDirectBookImportService>());
                Assert.IsAssignableFrom<IAppSettingsStore>(provider.GetRequiredService<IAppSettingsStore>());
                Assert.IsAssignableFrom<IAppSettingsService>(provider.GetRequiredService<IAppSettingsService>());
                Assert.IsAssignableFrom<IAudioCacheLimitProvider>(provider.GetRequiredService<IAudioCacheLimitProvider>());
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
                Assert.IsAssignableFrom<ICacheWorkspaceService>(provider.GetRequiredService<ICacheWorkspaceService>());
                Assert.IsAssignableFrom<IAudioCacheProtectionRegistry>(provider.GetRequiredService<IAudioCacheProtectionRegistry>());
                Assert.IsAssignableFrom<IPrefetchScheduler>(provider.GetRequiredService<IPrefetchScheduler>());
                Assert.IsAssignableFrom<IReadingProgressStore>(provider.GetRequiredService<IReadingProgressStore>());
                Assert.IsAssignableFrom<TimeProvider>(provider.GetRequiredService<TimeProvider>());
                Assert.IsType<MainWindow>(provider.GetRequiredService<MainWindow>());

                Assert.Same(
                    provider.GetRequiredService<IPlaybackCoordinator>(),
                    provider.GetRequiredService<IPlaybackCoordinator>());
                Assert.Same(
                    provider.GetRequiredService<IAppSettingsService>(),
                    provider.GetRequiredService<IAppSettingsService>());
                Assert.Same(
                    provider.GetRequiredService<IAppSettingsService>(),
                    provider.GetRequiredService<IAudioCacheLimitProvider>());
                Assert.Same(
                    provider.GetRequiredService<IAppSettingsService>(),
                    provider.GetRequiredService<IBookFileNameTemplateProvider>());
                Assert.Same(
                    provider.GetRequiredService<IAppSettingsService>(),
                    provider.GetRequiredService<ITextSegmentationOptionsProvider>());
                Assert.Same(
                    provider.GetRequiredService<IAudioCache>(),
                    provider.GetRequiredService<IAudioCacheManagementService>());
                Assert.Same(
                    provider.GetRequiredService<INavigationGuardService>(),
                    provider.GetRequiredService<INavigationGuardService>());
                Assert.NotSame(
                    provider.GetRequiredService<BookDetailsViewModel>(),
                    provider.GetRequiredService<BookDetailsViewModel>());
                Assert.NotSame(
                    provider.GetRequiredService<BookDetailsPage>(),
                    provider.GetRequiredService<BookDetailsPage>());
            }
            finally
            {
                provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        });
    }

    [Fact]
    public void Registration_methods_are_idempotent()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNovelSpeakerApplication();
        services.AddNovelSpeakerInfrastructure();
        services.AddNovelSpeakerDesktop();
        var descriptorCount = services.Count;

        services.AddNovelSpeakerApplication();
        services.AddNovelSpeakerInfrastructure();
        services.AddNovelSpeakerDesktop();

        Assert.Equal(descriptorCount, services.Count);
    }
}
