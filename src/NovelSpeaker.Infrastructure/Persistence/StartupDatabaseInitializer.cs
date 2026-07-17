using NovelSpeaker.Application.Abstractions;
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

    public StartupDatabaseInitializer(
        IAppDataDirectoryProvider directories,
        SqliteMigrationRunner migrationRunner,
        DefaultChapterRuleSeeder chapterRuleSeeder,
        BookOperationRecoveryService? operationRecovery = null,
        AppStoragePathMigrationService? pathMigration = null)
    {
        _directories = directories;
        _migrationRunner = migrationRunner;
        _chapterRuleSeeder = chapterRuleSeeder;
        _operationRecovery = operationRecovery;
        _pathMigration = pathMigration;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await _directories.EnsureCreatedAsync(cancellationToken);
        await _migrationRunner.InitializeAsync(cancellationToken);
        if (_pathMigration is not null)
        {
            await _pathMigration.MigrateAsync(cancellationToken);
        }

        if (_operationRecovery is not null)
        {
            await _operationRecovery.RecoverAsync(cancellationToken);
        }

        await _chapterRuleSeeder.SeedAsync(cancellationToken);
    }
}
