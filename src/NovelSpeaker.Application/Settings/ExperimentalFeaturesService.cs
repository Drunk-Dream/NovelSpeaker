namespace NovelSpeaker.Application.Settings;

public sealed record ExperimentalFeatureDefinition(string Id, string Name, string Description);
public sealed record ExperimentalFeatureChange(string FeatureId, bool Enabled);

/// <summary>Queries and persists feature IDs; feature-specific effects stay in their owning module.</summary>
public sealed class ExperimentalFeaturesService(IAppSettingsService settings)
{
    public const string MicrosoftEdgeTts = "microsoft-edge-tts";
    public static IReadOnlyList<ExperimentalFeatureDefinition> Registered { get; } = Array.AsReadOnly(new[]
    {
        new ExperimentalFeatureDefinition(MicrosoftEdgeTts, "Microsoft Edge 语音服务",
            "使用在线大声朗读服务。此功能仍在实验中，服务可用性可能变化。")
    });

    public bool IsEnabled(string featureId) =>
        settings.Current.EnabledExperimentalFeatureIds?.Contains(featureId, StringComparer.Ordinal) == true;

    public event EventHandler<AppSettingsChangedEventArgs>? Changed
    {
        add => settings.Changed += value;
        remove => settings.Changed -= value;
    }

    public async Task SetEnabledAsync(string featureId, bool enabled, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(featureId);
        cancellationToken.ThrowIfCancellationRequested();
        await settings.UpdateAsync(new AppSettingsUpdate
        {
            ExperimentalFeatureChange = new ExperimentalFeatureChange(featureId, enabled)
        }, cancellationToken).ConfigureAwait(false);
    }
}
