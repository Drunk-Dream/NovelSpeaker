using CommunityToolkit.Mvvm.ComponentModel;

namespace NovelSpeaker.App.ViewModels;

public sealed partial class PlayerViewModel : ObservableObject
{
    [ObservableProperty]
    private string headline = "播放页将在后续纵向切片中接入真实播放流程。";
}
