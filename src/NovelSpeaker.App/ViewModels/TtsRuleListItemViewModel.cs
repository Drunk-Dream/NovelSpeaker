using CommunityToolkit.Mvvm.ComponentModel;

namespace NovelSpeaker.App.ViewModels;

public sealed partial class TtsRuleListItemViewModel : ObservableObject
{
    public TtsRuleListItemViewModel(long id, string name, bool isEnabled, bool isCurrent, bool isSelected)
    {
        Id = id;
        Name = name;
        IsEnabled = isEnabled;
        IsCurrent = isCurrent;
        this.isSelected = isSelected;
    }

    public long Id { get; }

    public string Name { get; }

    public bool IsEnabled { get; }

    public bool IsCurrent { get; }

    [ObservableProperty]
    private bool isSelected;

    public string AutomationName =>
        $"{Name}，{(IsEnabled ? "已启用" : "已禁用")}{(IsCurrent ? "，当前规则" : string.Empty)}{(IsSelected ? "，已选中" : string.Empty)}";
}
