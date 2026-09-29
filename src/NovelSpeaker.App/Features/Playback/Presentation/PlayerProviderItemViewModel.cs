using NovelSpeaker.Domain.Speech.Providers;
using CommunityToolkit.Mvvm.ComponentModel;

namespace NovelSpeaker.App.Features.Playback.Presentation;

public sealed partial class PlayerProviderItemViewModel : ObservableObject
{
    public PlayerProviderItemViewModel(
        ProviderId id,
        string name,
        bool isSelected)
    {
        Id = id;
        Name = name;
        this.isSelected = isSelected;
    }

    public ProviderId Id { get; }

    public string Name { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCurrent))]
    private bool isSelected;

    public bool IsCurrent => IsSelected;
}
