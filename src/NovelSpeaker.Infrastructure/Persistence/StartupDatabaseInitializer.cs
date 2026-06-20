using NovelSpeaker.Application.Abstractions;

namespace NovelSpeaker.Infrastructure.Persistence;

/// <summary>
/// Ensures startup storage exists before schema initialization runs.
/// </summary>
public sealed class StartupDatabaseInitializer : IDatabaseInitializer
{
    private readonly IAppDataDirectoryProvider _directories;
    private readonly SqliteMigrationRunner _migrationRunner;
    private readonly DefaultChapterRuleSeeder _chapterRuleSeeder;

    public StartupDatabaseInitializer(
        IAppDataDirectoryProvider directories,
        SqliteMigrationRunner migrationRunner,
        DefaultChapterRuleSeeder chapterRuleSeeder)
    {
        _directories = directories;
        _migrationRunner = migrationRunner;
        _chapterRuleSeeder = chapterRuleSeeder;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await _directories.EnsureCreatedAsync(cancellationToken);
        await _migrationRunner.InitializeAsync(cancellationToken);
        await _chapterRuleSeeder.SeedAsync(cancellationToken);
    }
}
