using CommunityToolkit.Mvvm.ComponentModel;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Application.Speech.Providers;
using NovelSpeaker.App.Features.Settings;
using NovelSpeaker.App.Shared.Feedback;
using NovelSpeaker.App.Shell.Navigation;

namespace NovelSpeaker.App.Features.ExperimentalFeatures;

public sealed class ExperimentalFeaturesViewModel(
    ExperimentalFeaturesService features,
    SpeechProviderWorkspace providers,
    IAppNavigator navigator,
    IAppFeedbackService feedback) : SettingsSubpageViewModelBase(navigator, feedback)
{
    public IReadOnlyList<ExperimentalFeatureItemViewModel> Features { get; private set; } = [];

    public override Task LoadAsync(CancellationToken cancellationToken)
    {
        Activate(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        Features = Array.AsReadOnly(ExperimentalFeaturesService.Registered.Select(definition =>
            new ExperimentalFeatureItemViewModel(definition, features.IsEnabled(definition.Id), SetEnabled)).ToArray());
        OnPropertyChanged(nameof(Features));
        return Task.CompletedTask;
    }

    private void SetEnabled(ExperimentalFeatureItemViewModel item, bool enabled)
    {
        if (item.IsSaving) return;
        item.IsSaving = true;
        RunPageOperation("保存实验性功能失败", async token =>
        {
            try
            {
                if (item.Id == ExperimentalFeaturesService.MicrosoftEdgeTts)
                    await providers.SetEdgeEnabledAsync(enabled, token);
                else
                    await features.SetEnabledAsync(item.Id, enabled, token);
            }
            finally
            {
                if (IsCurrentActivation(token))
                {
                    item.Apply(features.IsEnabled(item.Id));
                    item.IsSaving = false;
                }
            }
        });
    }
}

public sealed partial class ExperimentalFeatureItemViewModel : ObservableObject
{
    private readonly Action<ExperimentalFeatureItemViewModel, bool> _setEnabled;
    private bool _isApplying;

    public ExperimentalFeatureItemViewModel(ExperimentalFeatureDefinition definition, bool enabled,
        Action<ExperimentalFeatureItemViewModel, bool> setEnabled)
    {
        Id = definition.Id;
        Name = definition.Name;
        Description = definition.Description;
        isEnabled = enabled;
        _setEnabled = setEnabled;
    }

    public string Id { get; }
    public string Name { get; }
    public string Description { get; }
    public bool CanToggle => !IsSaving;
    [ObservableProperty] private bool isEnabled;
    [ObservableProperty] private bool isSaving;

    internal void Apply(bool enabled)
    {
        _isApplying = true;
        IsEnabled = enabled;
        _isApplying = false;
    }

    partial void OnIsEnabledChanged(bool value)
    {
        if (!_isApplying) _setEnabled(this, value);
    }
    partial void OnIsSavingChanged(bool value) => OnPropertyChanged(nameof(CanToggle));
}
