using System;
using System.Threading;
using System.Threading.Tasks;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Domain.Settings;

namespace NovelSpeaker.App.Theming;

public sealed class ThemePreferenceService : IThemePreferenceService
{
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private readonly IAppSettingsStore _settingsStore;
    private readonly AppThemeStartupCoordinator _themeCoordinator;
    private int _latestRequestId;

    public ThemePreferenceService(
        IAppSettingsStore settingsStore,
        AppThemeStartupCoordinator themeCoordinator)
    {
        _settingsStore = settingsStore;
        _themeCoordinator = themeCoordinator;
    }

    public async Task<ThemePreferenceChangeResult> ApplyAsync(string requestedTheme, CancellationToken cancellationToken)
    {
        var requestId = Interlocked.Increment(ref _latestRequestId);
        var fallbackTheme = AppSettings.DefaultTheme;

        _themeCoordinator.Apply(requestedTheme);

        await _saveLock.WaitAsync(cancellationToken);
        try
        {
            var currentSettings = await _settingsStore.LoadAsync(cancellationToken);
            fallbackTheme = currentSettings.Theme;

            await _settingsStore.SaveAsync(currentSettings with { Theme = requestedTheme }, cancellationToken);
            var persistedSettings = await _settingsStore.LoadAsync(cancellationToken);

            return new ThemePreferenceChangeResult(
                true,
                requestId != Volatile.Read(ref _latestRequestId),
                persistedSettings.Theme);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (requestId == Volatile.Read(ref _latestRequestId))
            {
                _themeCoordinator.Apply(fallbackTheme);
                return new ThemePreferenceChangeResult(false, false, fallbackTheme, exception);
            }

            return new ThemePreferenceChangeResult(false, true, fallbackTheme, exception);
        }
        finally
        {
            _saveLock.Release();
        }
    }
}
