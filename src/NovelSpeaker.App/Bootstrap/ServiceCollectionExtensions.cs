using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NovelSpeaker.App.Desktop.MediaControls;
using NovelSpeaker.App.Desktop.Lifecycle;
using NovelSpeaker.App.Features.Books.Shared;
using NovelSpeaker.App.Features.Appearance;
using NovelSpeaker.App.Features.Books.Details;
using NovelSpeaker.App.Features.Cache;
using NovelSpeaker.App.Features.Rules.Chapter;
using NovelSpeaker.App.Features.Diagnostics;
using NovelSpeaker.App.Features.GeneralSettings;
using NovelSpeaker.App.Features.ImportTextSettings;
using NovelSpeaker.App.Features.Books.Library;
using NovelSpeaker.App.Features.Playback;
using NovelSpeaker.App.Features.PlaybackSettings;
using NovelSpeaker.App.Features.Rules.Regex;
using NovelSpeaker.App.Features.Settings;
using NovelSpeaker.App.Features.Rules.Tts;
using NovelSpeaker.App.Shared;
using NovelSpeaker.App.Shell;
using NovelSpeaker.App.Shell.Activation;

namespace NovelSpeaker.App.Bootstrap;

/// <summary>
/// Composes the desktop feature registration modules at the application boundary.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddNovelSpeakerDesktop(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IProcessShutdownGate, ProcessShutdownGate>();
        return services
            .AddDesktopLifecycle()
            .AddMediaControls()
            .AddSharedServices()
            .AddBooksSharedFeature()
            .AddShellServices()
            .AddAppearanceFeature()
            .AddBookDetailsFeature()
            .AddCacheFeature()
            .AddChapterRulesFeature()
            .AddDiagnosticsFeature()
            .AddGeneralSettingsFeature()
            .AddImportTextSettingsFeature()
            .AddLibraryFeature()
            .AddPlaybackFeature()
            .AddPlaybackSettingsFeature()
            .AddRegexReplacementRulesFeature()
            .AddSettingsFeature()
            .AddTtsRulesFeature();
    }
}
