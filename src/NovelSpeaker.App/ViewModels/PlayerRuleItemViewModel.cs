using CommunityToolkit.Mvvm.ComponentModel;

namespace NovelSpeaker.App.ViewModels;

public sealed partial class PlayerRuleItemViewModel : ObservableObject
{
    public PlayerRuleItemViewModel(
        long id,
        string name,
        bool isEnabled,
        bool isSelected)
    {
        Id = id;
        Name = name;
        IsEnabled = isEnabled;
        this.isSelected = isSelected;
    }

    public long Id { get; }

    public string Name { get; }

    public bool IsEnabled { get; }

    [ObservableProperty]
    private bool isSelected;
}
