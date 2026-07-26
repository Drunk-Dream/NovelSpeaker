using CommunityToolkit.Mvvm.ComponentModel;
using NovelSpeaker.Domain.Books;

namespace NovelSpeaker.App.Features.RegexReplacementRules;

public sealed partial class RegexReplacementRuleListItemViewModel : ObservableObject
{
    public RegexReplacementRuleListItemViewModel(Guid id, string name, string patternSummary, bool isEnabled, RegexReplacementScope scope, bool isSelected, string? errorMessage)
    {
        Id = id; Name = name; PatternSummary = patternSummary; this.isEnabled = isEnabled; Scope = scope; this.isSelected = isSelected; ErrorMessage = errorMessage;
    }

    public Guid Id { get; }
    public string Name { get; }
    public string PatternSummary { get; }
    public RegexReplacementScope Scope { get; }
    public string? ErrorMessage { get; }
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public string ScopeDisplayName => Scope switch
    {
        RegexReplacementScope.Display => "仅显示",
        RegexReplacementScope.Speech => "仅朗读",
        _ => "显示与朗读"
    };
    public string EnabledStateText => IsEnabled ? "已启用" : "已禁用";
    [ObservableProperty] private bool isEnabled;
    [ObservableProperty] private bool isSelected;
    [ObservableProperty] private bool isDropTarget;
    [ObservableProperty] private bool canQuickActions = true;
    [ObservableProperty] private bool canMoveUp = true;
    [ObservableProperty] private bool canMoveDown = true;

    public bool CanDeleteAction => CanQuickActions;

    public string AutomationName => $"{Name}，{EnabledStateText}，{ScopeDisplayName}{(HasError ? "，规则错误" : string.Empty)}{(IsSelected ? "，已选中" : string.Empty)}";
    partial void OnIsEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(EnabledStateText));
        OnPropertyChanged(nameof(AutomationName));
    }
    partial void OnIsSelectedChanged(bool value) => OnPropertyChanged(nameof(AutomationName));
    partial void OnCanQuickActionsChanged(bool value) => OnPropertyChanged(nameof(CanDeleteAction));
}
