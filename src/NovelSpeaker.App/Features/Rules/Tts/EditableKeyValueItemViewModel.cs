using CommunityToolkit.Mvvm.ComponentModel;

namespace NovelSpeaker.App.Features.Rules.Tts;

public sealed partial class EditableKeyValueItemViewModel : ObservableObject
{
    public EditableKeyValueItemViewModel(string key = "", string value = "")
    {
        this.key = key;
        this.value = value;
    }

    [ObservableProperty]
    private string key;

    [ObservableProperty]
    private string value;
}
