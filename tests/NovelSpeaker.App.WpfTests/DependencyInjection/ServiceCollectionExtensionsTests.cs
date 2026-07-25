using Microsoft.Extensions.DependencyInjection;
using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.DependencyInjection;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Playback.Cache;
using NovelSpeaker.Application.Playback.Audio;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Application.Speech;
using NovelSpeaker.Application.Speech.Rules;
using NovelSpeaker.Application.Speech.Execution;
using NovelSpeaker.Application.Speech.Testing;
using NovelSpeaker.App;
using NovelSpeaker.App.Features.Diagnostics;
using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.App.Features.Library;
using NovelSpeaker.App.Shell.Navigation;
using NovelSpeaker.App.Shell.Activation;
using NovelSpeaker.App.Shell.Input;
using NovelSpeaker.App.Features.Playback.Scrolling;
using NovelSpeaker.App.Shell;
using NovelSpeaker.App.Shared.Theming;
using NovelSpeaker.Infrastructure.DependencyInjection;
using NovelSpeaker.Infrastructure.Diagnostics;
using NovelSpeaker.Infrastructure.Playback;
using NovelSpeaker.Infrastructure.Speech.Http;
using System.Reflection;
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
                Assert.IsAssignableFrom<IAppNavigator>(provider.GetRequiredService<IAppNavigator>());
                Assert.IsAssignableFrom<IShellNavigationAdapter>(provider.GetRequiredService<IShellNavigationAdapter>());
                Assert.IsAssignableFrom<IShellActivationCoordinator>(provider.GetRequiredService<IShellActivationCoordinator>());
                Assert.IsAssignableFrom<IShellPlatformAdapter>(provider.GetRequiredService<IShellPlatformAdapter>());
                Assert.IsAssignableFrom<IShortcutContextResolver>(provider.GetRequiredService<IShortcutContextResolver>());
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
                Assert.IsAssignableFrom<ITtsRuleImportUseCase>(provider.GetRequiredService<ITtsRuleImportUseCase>());
                Assert.IsAssignableFrom<ITtsRuleEditorUseCase>(provider.GetRequiredService<ITtsRuleEditorUseCase>());
                Assert.IsAssignableFrom<ITtsRuleSelectionUseCase>(provider.GetRequiredService<ITtsRuleSelectionUseCase>());
                Assert.IsAssignableFrom<ITtsRuleQueries>(provider.GetRequiredService<ITtsRuleQueries>());
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
                Assert.IsType<LocalAudioPlaybackCoordinator>(provider.GetRequiredService<ILocalAudioPlaybackCoordinator>());
                Assert.IsType<PlaybackCoordinator>(provider.GetRequiredService<PlaybackCoordinator>());
                Assert.IsAssignableFrom<IBookPlaybackContentService>(provider.GetRequiredService<IBookPlaybackContentService>());
                Assert.IsType<SelectedTtsRuleProvider>(provider.GetRequiredService<ISelectedTtsRuleProvider>());
                Assert.IsAssignableFrom<IPlaybackAudioProvider>(provider.GetRequiredService<IPlaybackAudioProvider>());
                Assert.IsType<PlaybackAudioProvider>(provider.GetRequiredService<IPlaybackAudioProvider>());
                Assert.IsType<PlaybackSegmentRunner>(provider.GetRequiredService<PlaybackSegmentRunner>());
                Assert.IsType<PlaybackRecoveryPolicy>(provider.GetRequiredService<PlaybackRecoveryPolicy>());
                Assert.IsType<PlaybackAudioFailureReporter>(provider.GetRequiredService<IPlaybackAudioFailureReporter>());
                Assert.IsAssignableFrom<ITtsRateLimiter>(provider.GetRequiredService<ITtsRateLimiter>());
                Assert.IsType<TtsRuleTestService>(provider.GetRequiredService<ITtsRuleTestService>());
                Assert.IsType<TtsRuleTestFailureReporter>(provider.GetRequiredService<ITtsRuleTestFailureReporter>());
                Assert.IsAssignableFrom<IHttpTtsClient>(provider.GetRequiredService<IHttpTtsClient>());
                Assert.IsAssignableFrom<ITtsHttpTransport>(provider.GetRequiredService<ITtsHttpTransport>());
                Assert.IsAssignableFrom<ITtsRetryPolicy>(provider.GetRequiredService<ITtsRetryPolicy>());
                Assert.IsAssignableFrom<ITtsResponseValidator>(provider.GetRequiredService<ITtsResponseValidator>());
                Assert.IsAssignableFrom<IAudioCache>(provider.GetRequiredService<IAudioCache>());
                Assert.IsAssignableFrom<IAudioCacheStore>(provider.GetRequiredService<IAudioCacheStore>());
                Assert.IsAssignableFrom<ICacheWorkspaceService>(provider.GetRequiredService<ICacheWorkspaceService>());
                Assert.IsAssignableFrom<IAudioCacheProtectionRegistry>(provider.GetRequiredService<IAudioCacheProtectionRegistry>());
                Assert.IsType<PlaybackPrefetchController>(provider.GetRequiredService<IPlaybackPrefetchController>());
                Assert.IsAssignableFrom<IReadingProgressStore>(provider.GetRequiredService<IReadingProgressStore>());
                Assert.IsAssignableFrom<TimeProvider>(provider.GetRequiredService<TimeProvider>());
                Assert.IsType<MainWindow>(provider.GetRequiredService<MainWindow>());

                Assert.Same(
                    provider.GetRequiredService<PlaybackCoordinator>(),
                    provider.GetRequiredService<IPlaybackSnapshotSource>());
                Assert.Same(
                    provider.GetRequiredService<IPlaybackSession>(),
                    provider.GetRequiredService<IPlaybackBookCommands>());
                Assert.Same(
                    provider.GetRequiredService<IPlaybackSession>(),
                    provider.GetRequiredService<IPlaybackRegexReplacementRefresher>());
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
                    provider.GetRequiredService<IAudioCacheStore>());
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

    [Fact]
    public void Application_registration_owns_all_application_use_cases()
    {
        var services = new ServiceCollection();

        services.AddNovelSpeakerApplication();

        var applicationAssembly =
            typeof(NovelSpeaker.Application.DependencyInjection.ServiceCollectionExtensions).Assembly;
        var expectedUseCases = new[]
        {
            typeof(IChapterRuleManagementService),
            typeof(IDirectBookImportService),
            typeof(IBookDeletionService),
            typeof(IChapterRuleWorkspaceService),
            typeof(IRegexReplacementRuleWorkspaceService),
            typeof(IRegexReplacementPipeline),
            typeof(ITextSegmenter),
            typeof(ITtsRuleQueries),
            typeof(ITtsRuleSelectionUseCase),
            typeof(ITtsRuleEditorUseCase),
            typeof(ITtsRuleImportUseCase),
            typeof(IHttpTtsClient),
            typeof(ITtsRuleTestService),
            typeof(IBookPlaybackContentService),
            typeof(ICacheWorkspaceService),
            typeof(IPlaybackAudioProvider),
            typeof(ILocalAudioPlaybackCoordinator),
            typeof(IPlaybackPrefetchController),
            typeof(IPlaybackSession),
            typeof(IPlaybackBookCommands),
            typeof(IPlaybackRegexReplacementRefresher),
            typeof(ISelectedTtsRuleProvider),
            typeof(IAppSettingsService)
        };

        foreach (var useCase in expectedUseCases)
        {
            var descriptor = Assert.Single(services, candidate => candidate.ServiceType == useCase);
            Assert.Equal(applicationAssembly, GetImplementationAssembly(descriptor));
        }
    }

    [Fact]
    public void Infrastructure_registration_contains_only_infrastructure_adapters()
    {
        var services = new ServiceCollection();

        services.AddNovelSpeakerInfrastructure();

        var infrastructureAssembly =
            typeof(NovelSpeaker.Infrastructure.DependencyInjection.ServiceCollectionExtensions).Assembly;
        Assert.NotEmpty(services);
        Assert.All(
            services,
            descriptor => Assert.Equal(infrastructureAssembly, GetImplementationAssembly(descriptor)));
        Assert.Contains(
            services,
            descriptor =>
                descriptor.ServiceType == typeof(Microsoft.Extensions.Logging.ILoggerProvider) &&
                descriptor.ImplementationType == typeof(RollingFileLoggerProvider));
    }

    private static Assembly GetImplementationAssembly(ServiceDescriptor descriptor)
    {
        if (descriptor.ImplementationType is not null)
        {
            return descriptor.ImplementationType.Assembly;
        }

        if (descriptor.ImplementationInstance is not null)
        {
            return descriptor.ImplementationInstance.GetType().Assembly;
        }

        return descriptor.ImplementationFactory?.Method.DeclaringType?.Assembly
            ?? throw new InvalidOperationException(
                $"Registration for {descriptor.ServiceType.FullName} has no implementation owner.");
    }
}
