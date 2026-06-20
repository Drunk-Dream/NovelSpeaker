using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Domain.Settings;

namespace NovelSpeaker.App.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly IAppSettingsStore _settingsStore;

    public SettingsViewModel(IAppSettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
    }

    [ObservableProperty]
    private bool enableLongParagraphSplitting;

    [ObservableProperty]
    private int longParagraphThreshold;

    [ObservableProperty]
    private string statusMessage = "在这里配置导入与文本分段偏好。";

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        var settings = await _settingsStore.LoadAsync(cancellationToken);
        EnableLongParagraphSplitting = settings.EnableLongParagraphSplitting;
        LongParagraphThreshold = settings.LongParagraphThreshold;
    }

    [RelayCommand]
    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        var settings = new AppSettings(
            EnableLongParagraphSplitting,
            LongParagraphThreshold);

        await _settingsStore.SaveAsync(settings, cancellationToken);
        var normalized = await _settingsStore.LoadAsync(cancellationToken);
        EnableLongParagraphSplitting = normalized.EnableLongParagraphSplitting;
        LongParagraphThreshold = normalized.LongParagraphThreshold;
        StatusMessage = "文本分段设置已保存。";
    }
}
