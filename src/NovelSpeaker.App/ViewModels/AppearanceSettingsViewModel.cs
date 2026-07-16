using CommunityToolkit.Mvvm.ComponentModel;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.App.Feedback;
using NovelSpeaker.App.Theming;
using NovelSpeaker.Domain.Settings;
using Wpf.Ui;

namespace NovelSpeaker.App.ViewModels;

public sealed partial class AppearanceSettingsViewModel : SettingsSubpageViewModelBase
{
    private readonly IAppSettingsService _settingsService;
    private readonly IThemePreferenceService _themePreferenceService;
    private bool _isLoading;
    private bool _isUpdatingThemeSelection;
    private int _themeSelectionVersion;

    public AppearanceSettingsViewModel(
        IAppSettingsService settingsService,
        IThemePreferenceService themePreferenceService,
        INavigationService navigationService,
        IAppFeedbackService feedbackService)
        : base(navigationService, feedbackService)
    {
        _settingsService = settingsService;
        _themePreferenceService = themePreferenceService;
    }

    public IReadOnlyList<string> AvailableThemes => AppSettings.SupportedThemes;

    [ObservableProperty]
    private string selectedTheme = AppSettings.DefaultTheme;

    public override async Task LoadAsync(CancellationToken cancellationToken)
    {
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

    partial void OnSelectedThemeChanged(string value)
    {
        if (_isLoading || _isUpdatingThemeSelection)
        {
            return;
        }

        var version = Interlocked.Increment(ref _themeSelectionVersion);
        _ = ApplyThemeSelectionAsync(value, version);
    }

    private async Task ApplyThemeSelectionAsync(string selectedThemeValue, int version)
    {
        try
        {
            var result = await _themePreferenceService.ApplyAsync(selectedThemeValue, CancellationToken.None).ConfigureAwait(false);
            if (result.IsStale || version != Volatile.Read(ref _themeSelectionVersion))
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
        catch (Exception exception)
        {
            ShowSaveFailure("主题切换失败", exception);
        }
    }

    private void SetSelectedThemeWithoutApplying(string theme)
    {
        _isUpdatingThemeSelection = true;
        SelectedTheme = theme;
        _isUpdatingThemeSelection = false;
    }
}
