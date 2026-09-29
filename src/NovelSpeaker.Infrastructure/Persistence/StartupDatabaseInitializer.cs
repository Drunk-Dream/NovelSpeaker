using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Application.Speech.Providers;
using NovelSpeaker.Infrastructure.Persistence.Books;

namespace NovelSpeaker.Infrastructure.Persistence;

/// <summary>
/// Ensures startup storage exists before schema initialization runs.
/// </summary>
public sealed class StartupDatabaseInitializer : IDatabaseInitializer
{
    private readonly IAppDataDirectoryProvider _directories;
    private readonly SqliteMigrationRunner _migrationRunner;
    private readonly DefaultChapterRuleSeeder _chapterRuleSeeder;
    private readonly BookOperationRecoveryService? _operationRecovery;
    private readonly AppStoragePathMigrationService? _pathMigration;
    private readonly AudioCacheFormatResetService? _audioCacheFormatReset;
    private readonly IAppSettingsService? _settingsService;
    private readonly IProviderStore? _providerStore;

    public StartupDatabaseInitializer(
        IAppDataDirectoryProvider directories,
        SqliteMigrationRunner migrationRunner,
        DefaultChapterRuleSeeder chapterRuleSeeder,
        BookOperationRecoveryService? operationRecovery = null,
        AppStoragePathMigrationService? pathMigration = null,
        AudioCacheFormatResetService? audioCacheFormatReset = null,
        IAppSettingsService? settingsService = null,
        IProviderStore? providerStore = null)
    {
        _directories = directories;
        _migrationRunner = migrationRunner;
        _chapterRuleSeeder = chapterRuleSeeder;
        _operationRecovery = operationRecovery;
        _pathMigration = pathMigration;
        _audioCacheFormatReset = audioCacheFormatReset;
        _settingsService = settingsService;
        _providerStore = providerStore;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await _directories.EnsureCreatedAsync(cancellationToken);
        await _migrationRunner.InitializeAsync(cancellationToken);
        await ReconcileCurrentProviderAsync(cancellationToken);
        if (_pathMigration is not null)
        {
            await _pathMigration.MigrateAsync(cancellationToken);
        }

        if (_audioCacheFormatReset is not null)
        {
            await _audioCacheFormatReset.ResetIfPendingAsync(cancellationToken);
        }

        if (_operationRecovery is not null)
        {
            await _operationRecovery.RecoverAsync(cancellationToken);
        }

        await _chapterRuleSeeder.SeedAsync(cancellationToken);
    }

    private async Task ReconcileCurrentProviderAsync(CancellationToken cancellationToken)
    {
        if (_settingsService?.Current.CurrentProviderId is not { } currentProviderId || _providerStore is null)
        {
            return;
        }

        var provider = await _providerStore.GetByIdAsync(currentProviderId, cancellationToken).ConfigureAwait(false);
        if (provider is null || !ProviderConfigurationValidator.Validate(provider).IsValid ||
            (provider.Type == NovelSpeaker.Domain.Speech.Providers.SpeechProviderType.MicrosoftEdge &&
             _settingsService.Current.EnabledExperimentalFeatureIds?.Contains(ExperimentalFeaturesService.MicrosoftEdgeTts) != true))
        {
            await _settingsService.UpdateAsync(
                new AppSettingsUpdate { ClearCurrentProvider = true },
                cancellationToken).ConfigureAwait(false);
        }
    }
}
