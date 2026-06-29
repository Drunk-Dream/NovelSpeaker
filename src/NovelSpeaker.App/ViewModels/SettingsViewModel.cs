using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Domain.Settings;

namespace NovelSpeaker.App.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly IAppSettingsStore _settingsStore;

    public SettingsViewModel(IAppSettingsStore settingsStore, ChapterRulesViewModel? chapterRulesViewModel = null)
    {
        _settingsStore = settingsStore;
        ChapterRules = chapterRulesViewModel;
    }

    public IReadOnlyList<string> AvailableLogLevels => AppSettings.SupportedLogLevels;

    public IReadOnlyList<string> AvailableThemes => AppSettings.SupportedThemes;

    public ChapterRulesViewModel? ChapterRules { get; }

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

    [ObservableProperty]
    private bool isChapterRulesVisible;

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

        if (IsChapterRulesVisible && ChapterRules is not null)
        {
            await ChapterRules.LoadAsync(cancellationToken);
        }
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
    public async Task ToggleChapterRulesAsync(CancellationToken cancellationToken)
    {
        IsChapterRulesVisible = !IsChapterRulesVisible;
        if (IsChapterRulesVisible && ChapterRules is not null)
        {
            await ChapterRules.LoadAsync(cancellationToken);
        }
    }
}
