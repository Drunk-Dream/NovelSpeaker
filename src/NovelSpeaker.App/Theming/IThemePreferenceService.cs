using System.Threading;
using System.Threading.Tasks;

namespace NovelSpeaker.App.Theming;

public interface IThemePreferenceService
{
    Task<ThemePreferenceChangeResult> ApplyAsync(string requestedTheme, CancellationToken cancellationToken);
}
