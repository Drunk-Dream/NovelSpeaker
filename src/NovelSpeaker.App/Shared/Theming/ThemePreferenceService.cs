using System;
using System.Threading;
using System.Threading.Tasks;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Domain.Settings;

namespace NovelSpeaker.App.Shared.Theming;

public sealed class ThemePreferenceService : IThemePreferenceService, IThemeToggleService
{
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private readonly IAppSettingsService _settingsService;
    private readonly AppThemeStartupCoordinator _themeCoordinator;
    private int _latestRequestId;
    private int _settingsChangeObserved;
    private int _applyingRequestId;
    private string? _applyingTheme;

    public ThemePreferenceService(
        IAppSettingsService settingsService,
        AppThemeStartupCoordinator themeCoordinator)
    {
        _settingsService = settingsService;
        _themeCoordinator = themeCoordinator;
        _settingsService.Changed += OnSettingsChanged;
    }

    public AppTheme EffectiveTheme => _themeCoordinator.EffectiveTheme;

    public event EventHandler? EffectiveThemeChanged;

    public async Task<ThemePreferenceChangeResult> ToggleLightDarkAsync(CancellationToken cancellationToken)
    {
        var requestId = Interlocked.Increment(ref _latestRequestId);
        try
        {
            await _saveLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            NotifyIfCurrent(requestId);
            throw;
        }

        try
        {
            var targetTheme = EffectiveTheme == AppTheme.Dark ? "Light" : "Dark";
            return await ApplyCoreAsync(requestId, targetTheme, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _saveLock.Release();
        }
    }

    public async Task<ThemePreferenceChangeResult> ApplyAsync(string requestedTheme, CancellationToken cancellationToken)
    {
        var requestId = Interlocked.Increment(ref _latestRequestId);
        try
        {
            await _saveLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            NotifyIfCurrent(requestId);
            throw;
        }

        try
        {
            return await ApplyCoreAsync(requestId, requestedTheme, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _saveLock.Release();
        }
    }

    private async Task<ThemePreferenceChangeResult> ApplyCoreAsync(
        int requestId,
        string requestedTheme,
        CancellationToken cancellationToken)
    {
        var fallbackTheme = AppSettings.DefaultTheme;
        try
        {
            var currentSettings = _settingsService.Current;
            fallbackTheme = currentSettings.Theme;
            Volatile.Write(ref _settingsChangeObserved, 0);
            Volatile.Write(ref _applyingRequestId, requestId);
            _applyingTheme = requestedTheme;
            _themeCoordinator.Apply(requestedTheme);

            var persistedSettings = await _settingsService.UpdateAsync(
                new AppSettingsUpdate
                {
                    Theme = requestedTheme
                },
                cancellationToken).ConfigureAwait(false);

            var isStale = requestId != Volatile.Read(ref _latestRequestId);
            if (Interlocked.Exchange(ref _settingsChangeObserved, 0) == 0 && !isStale)
            {
                EffectiveThemeChanged?.Invoke(this, EventArgs.Empty);
            }

            return new ThemePreferenceChangeResult(
                true,
                isStale,
                persistedSettings.Theme);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (requestId == Volatile.Read(ref _latestRequestId))
            {
                _themeCoordinator.Apply(fallbackTheme);
                NotifyIfCurrent(requestId);
                return new ThemePreferenceChangeResult(false, false, fallbackTheme, exception);
            }

            return new ThemePreferenceChangeResult(false, true, fallbackTheme, exception);
        }
        catch (OperationCanceledException)
        {
            if (requestId == Volatile.Read(ref _latestRequestId))
            {
                _themeCoordinator.Apply(fallbackTheme);
                NotifyIfCurrent(requestId);
            }

            throw;
        }
        finally
        {
            _applyingTheme = null;
            Volatile.Write(ref _applyingRequestId, 0);
        }
    }

    private void OnSettingsChanged(object? sender, AppSettingsChangedEventArgs args)
    {
        if (string.Equals(args.Previous.Theme, args.Current.Theme, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!string.Equals(_applyingTheme, args.Current.Theme, StringComparison.OrdinalIgnoreCase))
        {
            _themeCoordinator.Apply(args.Current.Theme);
        }

        Volatile.Write(ref _settingsChangeObserved, 1);
        if (string.Equals(_applyingTheme, args.Current.Theme, StringComparison.OrdinalIgnoreCase) &&
            Volatile.Read(ref _applyingRequestId) != Volatile.Read(ref _latestRequestId))
        {
            return;
        }

        EffectiveThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    private void NotifyIfCurrent(int requestId)
    {
        if (requestId == Volatile.Read(ref _latestRequestId))
        {
            EffectiveThemeChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
