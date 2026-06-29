using Microsoft.Extensions.DependencyInjection;
using NovelSpeaker.App.Pages;

namespace NovelSpeaker.App.Navigation;

public sealed class AppPageResolver : IAppPageResolver
{
    private readonly IServiceProvider _serviceProvider;

    public AppPageResolver(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public object Resolve(AppNavigationEntry entry)
    {
        return entry.PageKind switch
        {
            AppPageKind.Library => _serviceProvider.GetRequiredService<LibraryPage>(),
            AppPageKind.SettingsHome => _serviceProvider.GetRequiredService<SettingsPage>(),
            AppPageKind.TtsRules => _serviceProvider.GetRequiredService<TtsRulesPage>(),
            AppPageKind.ChapterRules => _serviceProvider.GetRequiredService<ChapterRulesPage>(),
            AppPageKind.CacheManagement => _serviceProvider.GetRequiredService<CacheManagementPage>(),
            AppPageKind.Player => _serviceProvider.GetRequiredService<PlayerPage>(),
            AppPageKind.BookDetails => _serviceProvider.GetRequiredService<BookDetailsPage>(),
            _ => throw new ArgumentOutOfRangeException(nameof(entry))
        };
    }
}
