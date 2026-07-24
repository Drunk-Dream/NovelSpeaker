using System.Threading;
using System.Threading.Tasks;

namespace NovelSpeaker.App.Shared.Theming;

public interface IThemePreferenceService
{
    Task<ThemePreferenceChangeResult> ApplyAsync(string requestedTheme, CancellationToken cancellationToken);
}
