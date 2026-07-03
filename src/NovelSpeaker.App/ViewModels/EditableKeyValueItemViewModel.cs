using CommunityToolkit.Mvvm.ComponentModel;

namespace NovelSpeaker.App.ViewModels;

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
