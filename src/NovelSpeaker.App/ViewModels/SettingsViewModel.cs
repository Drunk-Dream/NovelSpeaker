using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.App.Navigation;
using NovelSpeaker.Domain.Settings;

namespace NovelSpeaker.App.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly IAppNavigationService _navigationService;
    private readonly IAppSettingsStore _settingsStore;

    public SettingsViewModel(IAppSettingsStore settingsStore, IAppNavigationService navigationService)
    {
        _settingsStore = settingsStore;
        _navigationService = navigationService;
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
        var settings = await _settingsStore.LoadAsync(cancellationToken);
        EnableLongParagraphSplitting = settings.EnableLongParagraphSplitting;
        LongParagraphThreshold = settings.LongParagraphThreshold;
        DefaultSpeakSpeed = settings.DefaultSpeakSpeed;
        PrefetchCount = settings.PrefetchCount;
        SelectedLogLevel = settings.LogLevel;
        SelectedTheme = settings.Theme;
        BookFileNameTemplate = settings.BookFileNameTemplate!;
    }

    [RelayCommand]
    public async Task SaveAsync(CancellationToken cancellationToken)
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
        SelectedTheme = normalized.Theme;
        BookFileNameTemplate = normalized.BookFileNameTemplate!;
        StatusMessage = "设置已保存。";
    }

    [RelayCommand]
    private void OpenTtsRules()
    {
        _navigationService.NavigateToSettings(SettingsSection.TtsRules);
    }

    [RelayCommand]
    private void OpenChapterRules()
    {
        _navigationService.NavigateToSettings(SettingsSection.ChapterRules);
    }

    [RelayCommand]
    private void OpenCacheManagement()
    {
        _navigationService.NavigateToSettings(SettingsSection.CacheManagement);
    }
}
