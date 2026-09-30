using NovelSpeaker.Domain.Speech.Providers;
using CommunityToolkit.Mvvm.ComponentModel;

namespace NovelSpeaker.App.Features.Playback.Presentation;

public sealed partial class PlayerProviderItemViewModel : ObservableObject
{
    public PlayerProviderItemViewModel(
        ProviderId id,
        string name,
        bool isCurrent)
    {
        Id = id;
        Name = name;
        this.isCurrent = isCurrent;
    }

    public ProviderId Id { get; }

    public string Name { get; }

    [ObservableProperty] private bool isCurrent;
}
