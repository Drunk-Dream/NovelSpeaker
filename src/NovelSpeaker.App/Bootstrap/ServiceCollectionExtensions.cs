using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NovelSpeaker.App.Desktop.MediaControls;
using NovelSpeaker.App.Features.Appearance;
using NovelSpeaker.App.Features.BookDetails;
using NovelSpeaker.App.Features.Cache;
using NovelSpeaker.App.Features.ChapterRules;
using NovelSpeaker.App.Features.Diagnostics;
using NovelSpeaker.App.Features.ImportTextSettings;
using NovelSpeaker.App.Features.Library;
using NovelSpeaker.App.Features.Playback;
using NovelSpeaker.App.Features.PlaybackSettings;
using NovelSpeaker.App.Features.RegexReplacementRules;
using NovelSpeaker.App.Features.Settings;
using NovelSpeaker.App.Features.TtsRules;
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
            .AddMediaControls()
            .AddSharedServices()
            .AddShellServices()
            .AddAppearanceFeature()
            .AddBookDetailsFeature()
            .AddCacheFeature()
            .AddChapterRulesFeature()
            .AddDiagnosticsFeature()
            .AddImportTextSettingsFeature()
            .AddLibraryFeature()
            .AddPlaybackFeature()
            .AddPlaybackSettingsFeature()
            .AddRegexReplacementRulesFeature()
            .AddSettingsFeature()
            .AddTtsRulesFeature();
    }
}
