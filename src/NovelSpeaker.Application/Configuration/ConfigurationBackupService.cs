using NovelSpeaker.Application.Books.TextProcessing;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Application.Speech.Providers;

namespace NovelSpeaker.Application.Configuration;

/// <summary>Owns a validated, immutable document awaiting the user's overwrite confirmation.</summary>
public sealed class ConfigurationRestorePlan
{
    internal ConfigurationRestorePlan(string json) => Json = json;
    internal string Json { get; }
}

public interface IConfigurationBackupService
{
    Task<string> CreateBackupAsync(CancellationToken cancellationToken);
    Task<ConfigurationRestorePlan> PrepareRestoreAsync(string json, CancellationToken cancellationToken);
    Task RestoreAsync(ConfigurationRestorePlan plan, CancellationToken cancellationToken);
}

/// <summary>Coordinates private snapshot persistence and the normal typed configuration owners.</summary>
public sealed class ConfigurationBackupService(
    IConfigurationSnapshotStore store,
    AppSettingsService settings,
    SpeechProviderWorkspace providers,
    RegexReplacementRuleWorkspaceService regexRules) : IConfigurationBackupService
{
    public Task<string> CreateBackupAsync(CancellationToken cancellationToken) =>
        Task.Run(() => providers.WithConfigurationLockAsync(() => settings.ReadConfigurationAsync(
            async (current, token) => ConfigurationBackupCodec.Write(await store.ReadAsync(current, token).ConfigureAwait(false)),
            cancellationToken), cancellationToken), cancellationToken);

    public Task<ConfigurationRestorePlan> PrepareRestoreAsync(string json, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ = ConfigurationBackupCodec.Read(json);
            cancellationToken.ThrowIfCancellationRequested();
            return new ConfigurationRestorePlan(json);
        }, cancellationToken);

    public Task RestoreAsync(ConfigurationRestorePlan plan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return Task.Run(() => providers.WithConfigurationLockAsync(async () =>
        {
            var snapshot = ConfigurationBackupCodec.Read(plan.Json);
            await settings.RestoreConfigurationAsync(snapshot.Settings,
                (previous, token) => store.ReplaceAsync(snapshot, previous, token), cancellationToken).ConfigureAwait(false);
            providers.NotifyConfigurationRestored();
            regexRules.NotifyConfigurationRestored();
            return true;
        }, cancellationToken), cancellationToken);
    }
}
