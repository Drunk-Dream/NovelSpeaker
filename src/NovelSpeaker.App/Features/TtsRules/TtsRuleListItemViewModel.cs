using CommunityToolkit.Mvvm.ComponentModel;

namespace NovelSpeaker.App.Features.TtsRules;

public sealed partial class TtsRuleListItemViewModel : ObservableObject
{
    public TtsRuleListItemViewModel(
        long id,
        string name,
        bool isEnabled,
        bool isCurrent,
        bool isSelected)
        : this(id, name, string.Empty, isEnabled, isCurrent, isSelected)
    {
    }

    public TtsRuleListItemViewModel(
        long id,
        string name,
        string requestSummary,
        bool isEnabled,
        bool isCurrent,
        bool isSelected)
    {
        Id = id;
        Name = name;
        RequestSummary = requestSummary;
        IsEnabled = isEnabled;
        IsCurrent = isCurrent;
        this.isSelected = isSelected;
    }

    public long Id { get; }

    public string Name { get; }

    public string RequestSummary { get; }

    [ObservableProperty]
    private bool isEnabled;

    public bool IsCurrent { get; }

    [ObservableProperty]
    private bool isSelected;

    public string AutomationName =>
        $"{Name}，{(IsEnabled ? "已启用" : "已禁用")}{(IsSelected ? "，已选中" : string.Empty)}";
}
