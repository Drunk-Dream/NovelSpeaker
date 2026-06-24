using CommunityToolkit.Mvvm.ComponentModel;

namespace NovelSpeaker.App.ViewModels;

/// <summary>
/// Exposes startup progress before the main shell is available.
/// </summary>
public sealed partial class StartupStatusViewModel : ObservableObject
{
    [ObservableProperty]
    private string title = "NovelSpeaker 正在启动";

    [ObservableProperty]
    private string statusText = "正在准备应用。";

    [ObservableProperty]
    private string detailText = "首次启动或升级后，应用可能需要几秒完成初始化。";
}
