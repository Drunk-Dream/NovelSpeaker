using CommunityToolkit.Mvvm.ComponentModel;

namespace NovelSpeaker.App.Features.Rules.Metadata;

public sealed partial class MetadataRuleRow : ObservableObject
{
    public MetadataRuleRow(MetadataRuleState state) => State = state;

    public MetadataRuleState State { get; }
    public string Id => State.Id;
    public string Name => State.Name;
    public string PatternSummary => State.Pattern;
    public bool IsEnabled => State.IsEnabled;

    [ObservableProperty]
    private bool isSelected;
}
