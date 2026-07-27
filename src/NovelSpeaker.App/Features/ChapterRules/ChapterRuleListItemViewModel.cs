using CommunityToolkit.Mvvm.ComponentModel;

namespace NovelSpeaker.App.Features.ChapterRules;

public sealed partial class ChapterRuleListItemViewModel : ObservableObject
{
    public ChapterRuleListItemViewModel(
        string id,
        string name,
        string patternSummary,
        bool isEnabled,
        bool isBuiltIn,
        bool isSelected)
        : this(id, name, patternSummary, isEnabled, isBuiltIn, !isBuiltIn, isSelected)
    {
    }

    public ChapterRuleListItemViewModel(
        string id,
        string name,
        string patternSummary,
        bool isEnabled,
        bool isBuiltIn,
        bool canDelete,
        bool isSelected)
    {
        Id = id;
        Name = name;
        PatternSummary = patternSummary;
        IsBuiltIn = isBuiltIn;
        CanDelete = canDelete;
        this.isEnabled = isEnabled;
        this.isSelected = isSelected;
    }

    public string Id { get; }

    public string Name { get; }

    public string PatternSummary { get; }

    public bool IsBuiltIn { get; }

    public bool CanDelete { get; }

    [ObservableProperty]
    private bool isEnabled;

    [ObservableProperty]
    private bool isSelected;

    [ObservableProperty]
    private bool isDropTarget;

    [ObservableProperty]
    private bool canQuickActions = true;

    [ObservableProperty]
    private bool canMoveUp = true;

    [ObservableProperty]
    private bool canMoveDown = true;

    public string AutomationName =>
        $"{Name}，{(IsEnabled ? "已启用" : "已禁用")}{(IsSelected ? "，已选中" : string.Empty)}";

    public bool CanDeleteAction => CanDelete && CanQuickActions;

    partial void OnIsEnabledChanged(bool value) => OnPropertyChanged(nameof(AutomationName));

    partial void OnIsSelectedChanged(bool value) => OnPropertyChanged(nameof(AutomationName));

    partial void OnCanQuickActionsChanged(bool value) => OnPropertyChanged(nameof(CanDeleteAction));
}
