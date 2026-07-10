using CommunityToolkit.Mvvm.ComponentModel;
using NovelSpeaker.Domain.Books;

namespace NovelSpeaker.App.ViewModels;

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
    [ObservableProperty] private bool isEnabled;
    [ObservableProperty] private bool isSelected;
    public string AutomationName => $"{Name}，{(IsEnabled ? "已启用" : "已禁用")}，{Scope}{(HasError ? "，规则错误" : string.Empty)}{(IsSelected ? "，已选中" : string.Empty)}";
    partial void OnIsEnabledChanged(bool value) => OnPropertyChanged(nameof(AutomationName));
    partial void OnIsSelectedChanged(bool value) => OnPropertyChanged(nameof(AutomationName));
}
