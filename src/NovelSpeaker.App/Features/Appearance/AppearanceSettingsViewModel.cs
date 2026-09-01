using CommunityToolkit.Mvvm.ComponentModel;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.App.Features.Settings;
using NovelSpeaker.App.Shared.Presentation.Platform;
using NovelSpeaker.App.Shared.Theming;
using NovelSpeaker.App.Shell.Navigation;
using NovelSpeaker.Domain.Settings;

namespace NovelSpeaker.App.Features.Appearance;

public sealed partial class AppearanceSettingsViewModel : SettingsSubpageViewModelBase
{
    private readonly IAppSettingsService _settingsService;
    private readonly IThemePreferenceService _themePreferenceService;
    private readonly IUiScheduler _uiScheduler;
    private bool _isLoading;
    private bool _isUpdatingThemeSelection;
    private bool _settingsSubscriptionActive;
    private int _themeSelectionVersion;

    public AppearanceSettingsViewModel(
        IAppSettingsService settingsService,
        IThemePreferenceService themePreferenceService,
        IAppNavigator navigator,
        IAppFeedbackService feedbackService,
        IUiScheduler? uiScheduler = null)
        : base(navigator, feedbackService)
    {
        _settingsService = settingsService;
        _themePreferenceService = themePreferenceService;
        _uiScheduler = uiScheduler ?? new WpfUiScheduler();
    }

    public IReadOnlyList<string> AvailableThemes => AppSettings.SupportedThemes;

    [ObservableProperty]
    private string selectedTheme = AppSettings.DefaultTheme;

    public override async Task LoadAsync(CancellationToken cancellationToken)
    {
        Activate(cancellationToken);
        SubscribeToSettingsChanges();
        _isLoading = true;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var settings = _settingsService.Current;
            SetSelectedThemeWithoutApplying(settings.Theme);
        }
        finally
        {
            _isLoading = false;
        }
    }

    public override void Deactivate()
    {
        if (_settingsSubscriptionActive)
        {
            _settingsService.Changed -= OnSettingsChanged;
            _settingsSubscriptionActive = false;
        }

        base.Deactivate();
    }

    partial void OnSelectedThemeChanged(string value)
    {
        if (_isLoading || _isUpdatingThemeSelection)
        {
            return;
        }

        var version = Interlocked.Increment(ref _themeSelectionVersion);
        RunPageOperation(
            "主题切换失败",
            cancellationToken => ApplyThemeSelectionAsync(value, version, cancellationToken));
    }

    private async Task ApplyThemeSelectionAsync(
        string selectedThemeValue,
        int version,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _themePreferenceService.ApplyAsync(selectedThemeValue, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsCurrentActivation(cancellationToken) ||
                result.IsStale ||
                version != Volatile.Read(ref _themeSelectionVersion))
            {
                return;
            }

            if (!result.IsSuccess)
            {
                SetSelectedThemeWithoutApplying(result.EffectiveTheme);
                ShowSaveFailure("主题切换失败", result.Exception ?? new InvalidOperationException("主题切换失败。"));
                return;
            }

            if (!string.Equals(SelectedTheme, result.EffectiveTheme, StringComparison.Ordinal))
            {
                SetSelectedThemeWithoutApplying(result.EffectiveTheme);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (IsCurrentActivation(cancellationToken) &&
                version == Volatile.Read(ref _themeSelectionVersion))
            {
                ShowSaveFailure("主题切换失败", exception);
            }
        }
    }

    private void SetSelectedThemeWithoutApplying(string theme)
    {
        _isUpdatingThemeSelection = true;
        try
        {
            SelectedTheme = theme;
        }
        finally
        {
            _isUpdatingThemeSelection = false;
        }
    }

    private void SubscribeToSettingsChanges()
    {
        if (_settingsSubscriptionActive)
        {
            return;
        }

        _settingsService.Changed += OnSettingsChanged;
        _settingsSubscriptionActive = true;
    }

    private void OnSettingsChanged(object? sender, AppSettingsChangedEventArgs args)
    {
        if (string.Equals(args.Previous.Theme, args.Current.Theme, StringComparison.OrdinalIgnoreCase) ||
            ActivationToken.IsCancellationRequested)
        {
            return;
        }

        var theme = args.Current.Theme;
        var activationToken = ActivationToken;
        if (_uiScheduler.CheckAccess())
        {
            if (IsCurrentActivation(activationToken))
            {
                SetSelectedThemeWithoutApplying(theme);
            }

            return;
        }

        RunPageOperation(
            "主题同步失败",
            cancellationToken => _uiScheduler.InvokeAsync(
                () =>
                {
                    if (IsCurrentActivation(activationToken))
                    {
                        SetSelectedThemeWithoutApplying(theme);
                    }
                },
                cancellationToken));
    }
}
