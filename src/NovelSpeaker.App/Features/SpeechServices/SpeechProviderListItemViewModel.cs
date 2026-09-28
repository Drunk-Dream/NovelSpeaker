using CommunityToolkit.Mvvm.ComponentModel;
using NovelSpeaker.Domain.Speech.Providers;

namespace NovelSpeaker.App.Features.SpeechServices;

public sealed partial class SpeechProviderListItemViewModel(
    ProviderId id, string name, SpeechProviderType type, bool current, bool selected) : ObservableObject
{
    public ProviderId Id { get; } = id;
    public string Name { get; } = name;
    public SpeechProviderType Type { get; } = type;
    public string TypeDisplayName => Type == SpeechProviderType.Http ? "HTTP" : "Microsoft Edge";
    public bool CanShare => Type == SpeechProviderType.Http;
    public bool CanDelete => Type == SpeechProviderType.Http;
    [ObservableProperty] private bool isSelected = selected;
    [ObservableProperty] private bool isCurrent = current;
    [ObservableProperty] private bool canMoveUp;
    [ObservableProperty] private bool canMoveDown;
}
