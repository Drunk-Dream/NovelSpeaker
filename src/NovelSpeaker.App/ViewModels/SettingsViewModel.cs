using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.App.Feedback;
using NovelSpeaker.App.Pages;
using NovelSpeaker.App.Theming;
using NovelSpeaker.Domain.Settings;
using Wpf.Ui;

namespace NovelSpeaker.App.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly INavigationService _navigationService;
    private readonly IAppSettingsStore _settingsStore;
    private readonly IThemePreferenceService _themePreferenceService;
    private readonly IAppFeedbackService _feedbackService;
    private bool _isLoading;
    private bool _isUpdatingThemeSelection;
    private int _themeSelectionVersion;

    public SettingsViewModel(
        IAppSettingsStore settingsStore,
        INavigationService navigationService,
        IThemePreferenceService themePreferenceService,
        IAppFeedbackService feedbackService)
    {
        _settingsStore = settingsStore;
        _navigationService = navigationService;
        _themePreferenceService = themePreferenceService;
        _feedbackService = feedbackService;
    }

    public IReadOnlyList<string> AvailableLogLevels => AppSettings.SupportedLogLevels;

    public IReadOnlyList<string> AvailableThemes => AppSettings.SupportedThemes;

    [ObservableProperty]
    private bool enableLongParagraphSplitting;

    [ObservableProperty]
    private int longParagraphThreshold;

    [ObservableProperty]
    private int defaultSpeakSpeed = AppSettings.DefaultSpeakSpeedValue;

    [ObservableProperty]
    private int prefetchCount = AppSettings.DefaultPrefetchCountValue;

    [ObservableProperty]
    private string selectedLogLevel = AppSettings.DefaultLogLevel;

    [ObservableProperty]
    private string selectedTheme = AppSettings.DefaultTheme;

    [ObservableProperty]
    private string bookFileNameTemplate = AppSettings.DefaultBookFileNameTemplate;

    [ObservableProperty]
    private string statusMessage = "在这里配置播放、导入与文本分段偏好。";

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        _isLoading = true;
        try
        {
            var settings = await _settingsStore.LoadAsync(cancellationToken);
            EnableLongParagraphSplitting = settings.EnableLongParagraphSplitting;
            LongParagraphThreshold = settings.LongParagraphThreshold;
            DefaultSpeakSpeed = settings.DefaultSpeakSpeed;
            PrefetchCount = settings.PrefetchCount;
            SelectedLogLevel = settings.LogLevel;
            SelectedTheme = settings.Theme;
            BookFileNameTemplate = settings.BookFileNameTemplate!;
        }
        finally
        {
            _isLoading = false;
        }
    }

    [RelayCommand]
    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            var currentSettings = await _settingsStore.LoadAsync(cancellationToken);
            var settings = currentSettings with
            {
                EnableLongParagraphSplitting = EnableLongParagraphSplitting,
                LongParagraphThreshold = LongParagraphThreshold,
                DefaultSpeakSpeed = DefaultSpeakSpeed,
                PrefetchCount = PrefetchCount,
                LogLevel = SelectedLogLevel,
                Theme = SelectedTheme,
                BookFileNameTemplate = BookFileNameTemplate
            };

            await _settingsStore.SaveAsync(settings, cancellationToken);
            var normalized = await _settingsStore.LoadAsync(cancellationToken);
            EnableLongParagraphSplitting = normalized.EnableLongParagraphSplitting;
            LongParagraphThreshold = normalized.LongParagraphThreshold;
            DefaultSpeakSpeed = normalized.DefaultSpeakSpeed;
            PrefetchCount = normalized.PrefetchCount;
            SelectedLogLevel = normalized.LogLevel;
            SetSelectedThemeWithoutApplying(normalized.Theme);
            BookFileNameTemplate = normalized.BookFileNameTemplate!;
            StatusMessage = "在这里配置播放、导入与文本分段偏好。";
        }
        catch (Exception exception)
        {
            var projected = _feedbackService.Project(exception);
            StatusMessage = projected.UserMessage;
            _feedbackService.ShowProjectedNotification("设置保存失败", projected);
        }
    }

    [RelayCommand]
    private void OpenTtsRules()
    {
        _navigationService.NavigateWithHierarchy(typeof(TtsRulesPage));
    }

    [RelayCommand]
    private void OpenChapterRules()
    {
        _navigationService.NavigateWithHierarchy(typeof(ChapterRulesPage));
    }

    [RelayCommand]
    private void OpenCacheManagement()
    {
        _navigationService.NavigateWithHierarchy(typeof(CacheManagementPage));
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

    private async Task ApplyThemeSelectionAsync(string selectedTheme, int version)
    {
        try
        {
            var result = await _themePreferenceService.ApplyAsync(selectedTheme, CancellationToken.None);
            if (result.IsStale || version != Volatile.Read(ref _themeSelectionVersion))
            {
                return;
            }

            if (!result.IsSuccess)
            {
                SetSelectedThemeWithoutApplying(result.EffectiveTheme);
                var projected = _feedbackService.Project(result.Exception!);
                StatusMessage = projected.UserMessage;
                _feedbackService.ShowProjectedNotification("主题切换失败", projected);
                return;
            }

            if (!string.Equals(SelectedTheme, result.EffectiveTheme, StringComparison.Ordinal))
            {
                SetSelectedThemeWithoutApplying(result.EffectiveTheme);
            }
        }
        catch (Exception exception)
        {
            var projected = _feedbackService.Project(exception);
            StatusMessage = projected.UserMessage;
            _feedbackService.ShowProjectedNotification("主题切换失败", projected);
        }
    }

    private void SetSelectedThemeWithoutApplying(string theme)
    {
        _isUpdatingThemeSelection = true;
        SelectedTheme = theme;
        _isUpdatingThemeSelection = false;
    }
}
