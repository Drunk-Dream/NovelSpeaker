# Epic B+C TXT Import And Chapter Splitting Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the first end-to-end TXT import slice for NovelSpeaker: import a local TXT file, detect its encoding, normalize text, detect duplicate files, split chapters with database-backed rules, persist `Books` and `Chapters` transactionally, and expose the workflow through the WPF library and rules pages.

**Architecture:** Keep the feature as a two-phase pipeline. `IBookImportService` coordinates `AnalyzeAsync` and `CommitAsync`, infrastructure services handle file reading, hashing, rule lookup, chapter splitting, file copy, and SQLite persistence, and the WPF layer stays thin by routing commands through view models and application-facing abstractions. The shell UI should move toward the structure in `docs/06_UI_AND_USER_FLOWS.md`: top navigation, page content, and a persistent bottom player bar.

**Tech Stack:** C#, .NET 10, WPF, CommunityToolkit.Mvvm, Microsoft.Data.Sqlite, Microsoft.Extensions.DependencyInjection, xUnit

---

## Scope Check

This spec spans import pipeline, chapter rule management, persistence, and shell UI, but the parts are not independent subsystems. They form one vertical slice: the user cannot successfully import a book without schema, rules, analyzer, splitter, commit path, and UI entry points. Keep this as one implementation plan.

## File Structure

### New files

- `src/NovelSpeaker.Domain/Books/Book.cs`
- `src/NovelSpeaker.Domain/Books/Chapter.cs`
- `src/NovelSpeaker.Domain/Books/ChapterRule.cs`
- `src/NovelSpeaker.Application/Books/BookImportAnalysis.cs`
- `src/NovelSpeaker.Application/Books/BookImportAnalysisStatus.cs`
- `src/NovelSpeaker.Application/Books/BookFileCopyHandle.cs`
- `src/NovelSpeaker.Application/Books/BookImportChapter.cs`
- `src/NovelSpeaker.Application/Books/BookImportFailureReason.cs`
- `src/NovelSpeaker.Application/Books/BookImportResult.cs`
- `src/NovelSpeaker.Application/Books/BookSummary.cs`
- `src/NovelSpeaker.Application/Books/TextFileAnalysis.cs`
- `src/NovelSpeaker.Application/Books/IBookCatalogService.cs`
- `src/NovelSpeaker.Application/Books/IBookDuplicateDetector.cs`
- `src/NovelSpeaker.Application/Books/IBookFileStore.cs`
- `src/NovelSpeaker.Application/Books/IBookImportRepository.cs`
- `src/NovelSpeaker.Application/Books/IBookImportService.cs`
- `src/NovelSpeaker.Application/Books/IChapterRuleRepository.cs`
- `src/NovelSpeaker.Application/Books/IChapterSplitter.cs`
- `src/NovelSpeaker.Application/Books/IContentHasher.cs`
- `src/NovelSpeaker.Application/Books/ITextFileAnalyzer.cs`
- `src/NovelSpeaker.Application/Books/ITextNormalizer.cs`
- `src/NovelSpeaker.Infrastructure/Books/BookImportService.cs`
- `src/NovelSpeaker.Infrastructure/Books/DefaultChapterRules.cs`
- `src/NovelSpeaker.Infrastructure/Books/FileStorage/BookFileStore.cs`
- `src/NovelSpeaker.Infrastructure/Books/Parsing/ChapterSplitter.cs`
- `src/NovelSpeaker.Infrastructure/Books/Text/Sha256ContentHasher.cs`
- `src/NovelSpeaker.Infrastructure/Books/Text/TextFileAnalyzer.cs`
- `src/NovelSpeaker.Infrastructure/Books/Text/TextNormalizer.cs`
- `src/NovelSpeaker.Infrastructure/Persistence/BookCatalogService.cs`
- `src/NovelSpeaker.Infrastructure/Persistence/BookDuplicateDetector.cs`
- `src/NovelSpeaker.Infrastructure/Persistence/BookImportRepository.cs`
- `src/NovelSpeaker.Infrastructure/Persistence/ChapterRuleRepository.cs`
- `src/NovelSpeaker.Infrastructure/Persistence/DefaultChapterRuleSeeder.cs`
- `src/NovelSpeaker.App/ViewModels/ChapterRulesViewModel.cs`
- `src/NovelSpeaker.App/ViewModels/LibraryBookItemViewModel.cs`
- `src/NovelSpeaker.App/ViewModels/LibraryViewModel.cs`
- `src/NovelSpeaker.App/ViewModels/PlayerViewModel.cs`
- `src/NovelSpeaker.App/ViewModels/SettingsViewModel.cs`
- `src/NovelSpeaker.App/Views/ChapterRulesView.xaml`
- `src/NovelSpeaker.App/Views/ChapterRulesView.xaml.cs`
- `src/NovelSpeaker.App/Views/LibraryView.xaml`
- `src/NovelSpeaker.App/Views/LibraryView.xaml.cs`
- `src/NovelSpeaker.App/Views/PlayerView.xaml`
- `src/NovelSpeaker.App/Views/PlayerView.xaml.cs`
- `src/NovelSpeaker.App/Views/SettingsView.xaml`
- `src/NovelSpeaker.App/Views/SettingsView.xaml.cs`
- `tests/NovelSpeaker.UnitTests/Books/BookImportServiceTests.cs`
- `tests/NovelSpeaker.UnitTests/Books/BookImportRepositoryTests.cs`
- `tests/NovelSpeaker.UnitTests/Books/BookFileStoreTests.cs`
- `tests/NovelSpeaker.UnitTests/Books/BookDuplicateDetectorTests.cs`
- `tests/NovelSpeaker.UnitTests/Books/ChapterRuleRepositoryTests.cs`
- `tests/NovelSpeaker.UnitTests/Books/ChapterSplitterTests.cs`
- `tests/NovelSpeaker.UnitTests/Books/Sha256ContentHasherTests.cs`
- `tests/NovelSpeaker.UnitTests/Books/TextFileAnalyzerTests.cs`
- `tests/NovelSpeaker.UnitTests/Books/TextNormalizerTests.cs`
- `tests/NovelSpeaker.UnitTests/ViewModels/ChapterRulesViewModelTests.cs`
- `tests/NovelSpeaker.UnitTests/ViewModels/LibraryViewModelTests.cs`

### Modified files

- `src/NovelSpeaker.App/App.xaml`
- `src/NovelSpeaker.App/MainWindow.xaml`
- `src/NovelSpeaker.App/MainWindow.xaml.cs`
- `src/NovelSpeaker.App/ServiceCollectionExtensions.cs`
- `src/NovelSpeaker.App/ViewModels/MainWindowViewModel.cs`
- `src/NovelSpeaker.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs`
- `src/NovelSpeaker.Infrastructure/Persistence/SqliteMigrationRunner.cs`
- `src/NovelSpeaker.Infrastructure/Persistence/StartupDatabaseInitializer.cs`
- `tests/NovelSpeaker.UnitTests/DependencyInjection/ServiceCollectionExtensionsTests.cs`
- `tests/NovelSpeaker.UnitTests/Persistence/SqliteMigrationRunnerTests.cs`

---

### Task 1: Add the import schema and book-domain records

**Files:**
- Create: `src/NovelSpeaker.Domain/Books/Book.cs`
- Create: `src/NovelSpeaker.Domain/Books/Chapter.cs`
- Create: `src/NovelSpeaker.Domain/Books/ChapterRule.cs`
- Modify: `src/NovelSpeaker.Infrastructure/Persistence/SqliteMigrationRunner.cs`
- Test: `tests/NovelSpeaker.UnitTests/Persistence/SqliteMigrationRunnerTests.cs`

- [ ] **Step 1: Write the failing migration test**

Add this test file content in `tests/NovelSpeaker.UnitTests/Persistence/SqliteMigrationRunnerTests.cs`:

```csharp
using NovelSpeaker.Infrastructure.FileSystem;
using NovelSpeaker.Infrastructure.Persistence;

namespace NovelSpeaker.UnitTests.Persistence;

public sealed class SqliteMigrationRunnerTests
{
    [Fact]
    public async Task InitializeAsync_creates_import_tables_and_advances_schema_version()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var directories = new LocalAppDataDirectoryProvider(root);
        var factory = new SqliteConnectionFactory(directories);
        var runner = new SqliteMigrationRunner(factory);
        var initializer = new StartupDatabaseInitializer(directories, runner);

        await initializer.InitializeAsync(CancellationToken.None);

        await using var connection = await factory.OpenConnectionAsync(CancellationToken.None);
        var tableCommand = connection.CreateCommand();
        tableCommand.CommandText =
            """
            SELECT COUNT(*)
            FROM sqlite_master
            WHERE type = 'table'
              AND name IN ('SchemaVersion', 'AppMetadata', 'Books', 'Chapters', 'ChapterRules');
            """;

        var tableCount = Convert.ToInt32(await tableCommand.ExecuteScalarAsync(CancellationToken.None));

        var versionCommand = connection.CreateCommand();
        versionCommand.CommandText = "SELECT COALESCE(MAX(Version), 0) FROM SchemaVersion;";
        var version = Convert.ToInt32(await versionCommand.ExecuteScalarAsync(CancellationToken.None));

        Assert.Equal(5, tableCount);
        Assert.Equal(2, version);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run:

```bash
dotnet test tests/NovelSpeaker.UnitTests/NovelSpeaker.UnitTests.csproj --filter InitializeAsync_creates_import_tables_and_advances_schema_version
```

Expected: FAIL because migration version `2` and the `Books` / `Chapters` / `ChapterRules` tables do not exist yet.

- [ ] **Step 3: Add the domain records and schema migration**

Create `src/NovelSpeaker.Domain/Books/Book.cs`:

```csharp
namespace NovelSpeaker.Domain.Books;

/// <summary>
/// Represents an imported book record that points to the stored original TXT file.
/// </summary>
public sealed record Book(
    string Id,
    string Title,
    string? Author,
    string OriginalFileName,
    string StoredFilePath,
    string SourceHash,
    string Encoding,
    string ImportedAt,
    string UpdatedAt);
```

Create `src/NovelSpeaker.Domain/Books/Chapter.cs`:

```csharp
namespace NovelSpeaker.Domain.Books;

/// <summary>
/// Represents a chapter persisted for a specific imported book.
/// </summary>
public sealed record Chapter(
    string Id,
    string BookId,
    int ChapterIndex,
    string Title,
    string Content,
    int StartOffset,
    int Length);
```

Create `src/NovelSpeaker.Domain/Books/ChapterRule.cs`:

```csharp
namespace NovelSpeaker.Domain.Books;

/// <summary>
/// Represents one database-backed chapter-detection rule.
/// </summary>
public sealed record ChapterRule(
    string Id,
    string Name,
    string Pattern,
    int SortOrder,
    bool IsEnabled,
    string CreatedAt,
    string UpdatedAt);
```

Update `src/NovelSpeaker.Infrastructure/Persistence/SqliteMigrationRunner.cs` so the migrations array becomes:

```csharp
private static readonly SqliteMigration[] Migrations =
[
    new(
        1,
        """
        CREATE TABLE IF NOT EXISTS SchemaVersion (
            Version INTEGER NOT NULL PRIMARY KEY
        );

        CREATE TABLE IF NOT EXISTS AppMetadata (
            Key TEXT NOT NULL PRIMARY KEY,
            Value TEXT NULL
        );
        """),
    new(
        2,
        """
        CREATE TABLE Books (
            Id TEXT NOT NULL PRIMARY KEY,
            Title TEXT NOT NULL,
            Author TEXT NULL,
            OriginalFileName TEXT NOT NULL,
            StoredFilePath TEXT NOT NULL,
            SourceHash TEXT NOT NULL,
            Encoding TEXT NOT NULL,
            ImportedAt TEXT NOT NULL,
            UpdatedAt TEXT NOT NULL
        );

        CREATE UNIQUE INDEX IX_Books_SourceHash
            ON Books(SourceHash);

        CREATE TABLE Chapters (
            Id TEXT NOT NULL PRIMARY KEY,
            BookId TEXT NOT NULL,
            ChapterIndex INTEGER NOT NULL,
            Title TEXT NOT NULL,
            Content TEXT NOT NULL,
            StartOffset INTEGER NOT NULL CHECK(StartOffset >= 0),
            Length INTEGER NOT NULL CHECK(Length > 0),
            FOREIGN KEY(BookId) REFERENCES Books(Id) ON DELETE CASCADE,
            UNIQUE(BookId, ChapterIndex)
        );

        CREATE TABLE ChapterRules (
            Id TEXT NOT NULL PRIMARY KEY,
            Name TEXT NOT NULL,
            Pattern TEXT NOT NULL,
            SortOrder INTEGER NOT NULL,
            IsEnabled INTEGER NOT NULL,
            CreatedAt TEXT NOT NULL,
            UpdatedAt TEXT NOT NULL
        );

        CREATE INDEX IX_ChapterRules_SortOrder
            ON ChapterRules(SortOrder);
        """)
];
```

- [ ] **Step 4: Run the migration test to verify it passes**

Run:

```bash
dotnet test tests/NovelSpeaker.UnitTests/NovelSpeaker.UnitTests.csproj --filter InitializeAsync_creates_import_tables_and_advances_schema_version
```

Expected: PASS. The database should now contain the three import tables and `SchemaVersion` should advance to `2`.

- [ ] **Step 5: Commit**

```bash
git add src/NovelSpeaker.Domain/Books src/NovelSpeaker.Infrastructure/Persistence/SqliteMigrationRunner.cs tests/NovelSpeaker.UnitTests/Persistence/SqliteMigrationRunnerTests.cs
git commit -m "feat(import): add book chapter and rule schema"
```

### Task 2: Add chapter-rule persistence and default-rule seeding

**Files:**
- Create: `src/NovelSpeaker.Application/Books/IChapterRuleRepository.cs`
- Create: `src/NovelSpeaker.Infrastructure/Books/DefaultChapterRules.cs`
- Create: `src/NovelSpeaker.Infrastructure/Persistence/ChapterRuleRepository.cs`
- Create: `src/NovelSpeaker.Infrastructure/Persistence/DefaultChapterRuleSeeder.cs`
- Modify: `src/NovelSpeaker.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs`
- Modify: `src/NovelSpeaker.Infrastructure/Persistence/StartupDatabaseInitializer.cs`
- Test: `tests/NovelSpeaker.UnitTests/Books/ChapterRuleRepositoryTests.cs`

- [ ] **Step 1: Write the failing default-rule test**

Create `tests/NovelSpeaker.UnitTests/Books/ChapterRuleRepositoryTests.cs`:

```csharp
using NovelSpeaker.Domain.Books;
using NovelSpeaker.Infrastructure.FileSystem;
using NovelSpeaker.Infrastructure.Persistence;

namespace NovelSpeaker.UnitTests.Books;

public sealed class ChapterRuleRepositoryTests
{
    [Fact]
    public async Task ImportDefaultsAsync_skips_exact_duplicates_and_preserves_existing_rows()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var directories = new LocalAppDataDirectoryProvider(root);
        var factory = new SqliteConnectionFactory(directories);
        var runner = new SqliteMigrationRunner(factory);
        var initializer = new StartupDatabaseInitializer(directories, runner);

        await initializer.InitializeAsync(CancellationToken.None);

        var repository = new ChapterRuleRepository(factory);
        var existing = new ChapterRule(
            Guid.NewGuid().ToString(),
            "章节数字",
            @"^\s*第[0-9一二三四五六七八九十百千零两]+章(?:\s+.+)?\s*$",
            90,
            false,
            DateTime.UtcNow.ToString("O"),
            DateTime.UtcNow.ToString("O"));

        await repository.SaveAsync(existing, CancellationToken.None);

        var insertedCount = await repository.ImportDefaultsAsync(CancellationToken.None);
        var allRules = await repository.GetAllAsync(CancellationToken.None);
        var preserved = allRules.Single(rule => rule.Id == existing.Id);

        Assert.True(insertedCount > 0);
        Assert.False(preserved.IsEnabled);
        Assert.Equal(1, allRules.Count(rule => rule.Name == existing.Name && rule.Pattern == existing.Pattern && rule.Id == existing.Id));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run:

```bash
dotnet test tests/NovelSpeaker.UnitTests/NovelSpeaker.UnitTests.csproj --filter ImportDefaultsAsync_skips_exact_duplicates_and_preserves_existing_rows
```

Expected: FAIL because `IChapterRuleRepository`, `ChapterRuleRepository`, and default-rule import do not exist yet.

- [ ] **Step 3: Implement the repository, defaults, and startup seeding**

Create `src/NovelSpeaker.Application/Books/IChapterRuleRepository.cs`:

```csharp
using NovelSpeaker.Domain.Books;

namespace NovelSpeaker.Application.Books;

/// <summary>
/// Owns persistence and ordering of chapter-detection rules.
/// </summary>
public interface IChapterRuleRepository
{
    Task<IReadOnlyList<ChapterRule>> GetAllAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<ChapterRule>> GetEnabledAsync(CancellationToken cancellationToken);
    Task SaveAsync(ChapterRule rule, CancellationToken cancellationToken);
    Task DeleteAsync(string ruleId, CancellationToken cancellationToken);
    Task MoveAsync(string ruleId, int newSortOrder, CancellationToken cancellationToken);
    Task<int> ImportDefaultsAsync(CancellationToken cancellationToken);
}
```

Create `src/NovelSpeaker.Infrastructure/Books/DefaultChapterRules.cs`:

```csharp
namespace NovelSpeaker.Infrastructure.Books;

internal static class DefaultChapterRules
{
    public static IReadOnlyList<(string Name, string Pattern)> All { get; } =
    [
        ("章节数字", @"^\s*第[0-9一二三四五六七八九十百千零两]+章(?:\s+.+)?\s*$"),
        ("章节卷标", @"^\s*第[0-9一二三四五六七八九十百千零两]+卷(?:\s+.+)?\s*$"),
        ("序章楔子", @"^\s*(序章|楔子|前言)\s*$"),
        ("尾声后记", @"^\s*(尾声|后记|番外(?:\s*.+)?)\s*$")
    ];
}
```

Create `src/NovelSpeaker.Infrastructure/Persistence/ChapterRuleRepository.cs`:

```csharp
using Microsoft.Data.Sqlite;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Domain.Books;
using NovelSpeaker.Infrastructure.Books;

namespace NovelSpeaker.Infrastructure.Persistence;

/// <summary>
/// Stores global chapter rules in SQLite.
/// </summary>
public sealed class ChapterRuleRepository : IChapterRuleRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public ChapterRuleRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<ChapterRule>> GetAllAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, Name, Pattern, SortOrder, IsEnabled, CreatedAt, UpdatedAt
            FROM ChapterRules
            ORDER BY SortOrder, Name;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var items = new List<ChapterRule>();

        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new ChapterRule(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.GetInt64(4) == 1,
                reader.GetString(5),
                reader.GetString(6)));
        }

        return items;
    }

    public async Task<IReadOnlyList<ChapterRule>> GetEnabledAsync(CancellationToken cancellationToken)
    {
        var rules = await GetAllAsync(cancellationToken);
        return rules.Where(rule => rule.IsEnabled).OrderBy(rule => rule.SortOrder).ToArray();
    }

    public async Task SaveAsync(ChapterRule rule, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO ChapterRules (Id, Name, Pattern, SortOrder, IsEnabled, CreatedAt, UpdatedAt)
            VALUES ($id, $name, $pattern, $sortOrder, $isEnabled, $createdAt, $updatedAt)
            ON CONFLICT(Id) DO UPDATE SET
                Name = excluded.Name,
                Pattern = excluded.Pattern,
                SortOrder = excluded.SortOrder,
                IsEnabled = excluded.IsEnabled,
                UpdatedAt = excluded.UpdatedAt;
            """;

        command.Parameters.AddWithValue("$id", rule.Id);
        command.Parameters.AddWithValue("$name", rule.Name);
        command.Parameters.AddWithValue("$pattern", rule.Pattern);
        command.Parameters.AddWithValue("$sortOrder", rule.SortOrder);
        command.Parameters.AddWithValue("$isEnabled", rule.IsEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$createdAt", rule.CreatedAt);
        command.Parameters.AddWithValue("$updatedAt", rule.UpdatedAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteAsync(string ruleId, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM ChapterRules WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", ruleId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task MoveAsync(string ruleId, int newSortOrder, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE ChapterRules
            SET SortOrder = $sortOrder,
                UpdatedAt = $updatedAt
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", ruleId);
        command.Parameters.AddWithValue("$sortOrder", newSortOrder);
        command.Parameters.AddWithValue("$updatedAt", DateTime.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<int> ImportDefaultsAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        var inserted = 0;

        foreach (var (name, pattern) in DefaultChapterRules.All)
        {
            var existsCommand = connection.CreateCommand();
            existsCommand.CommandText =
                """
                SELECT COUNT(*)
                FROM ChapterRules
                WHERE Name = $name AND Pattern = $pattern;
                """;
            existsCommand.Parameters.AddWithValue("$name", name);
            existsCommand.Parameters.AddWithValue("$pattern", pattern);

            var exists = Convert.ToInt32(await existsCommand.ExecuteScalarAsync(cancellationToken)) > 0;
            if (exists)
            {
                continue;
            }

            var now = DateTime.UtcNow.ToString("O");
            var insertCommand = connection.CreateCommand();
            insertCommand.CommandText =
                """
                INSERT INTO ChapterRules (Id, Name, Pattern, SortOrder, IsEnabled, CreatedAt, UpdatedAt)
                VALUES ($id, $name, $pattern, $sortOrder, 1, $createdAt, $updatedAt);
                """;
            insertCommand.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
            insertCommand.Parameters.AddWithValue("$name", name);
            insertCommand.Parameters.AddWithValue("$pattern", pattern);
            insertCommand.Parameters.AddWithValue("$sortOrder", inserted + 10);
            insertCommand.Parameters.AddWithValue("$createdAt", now);
            insertCommand.Parameters.AddWithValue("$updatedAt", now);
            await insertCommand.ExecuteNonQueryAsync(cancellationToken);
            inserted++;
        }

        return inserted;
    }
}
```

Create `src/NovelSpeaker.Infrastructure/Persistence/DefaultChapterRuleSeeder.cs`:

```csharp
namespace NovelSpeaker.Infrastructure.Persistence;

/// <summary>
/// Imports hard-coded default chapter rules after the schema is ready.
/// </summary>
public sealed class DefaultChapterRuleSeeder
{
    private readonly ChapterRuleRepository _repository;

    public DefaultChapterRuleSeeder(ChapterRuleRepository repository)
    {
        _repository = repository;
    }

    public Task SeedAsync(CancellationToken cancellationToken)
    {
        return _repository.ImportDefaultsAsync(cancellationToken);
    }
}
```

Update `src/NovelSpeaker.Infrastructure/Persistence/StartupDatabaseInitializer.cs`:

```csharp
public sealed class StartupDatabaseInitializer : IDatabaseInitializer
{
    private readonly IAppDataDirectoryProvider _directories;
    private readonly SqliteMigrationRunner _migrationRunner;
    private readonly DefaultChapterRuleSeeder _seeder;

    public StartupDatabaseInitializer(
        IAppDataDirectoryProvider directories,
        SqliteMigrationRunner migrationRunner,
        DefaultChapterRuleSeeder seeder)
    {
        _directories = directories;
        _migrationRunner = migrationRunner;
        _seeder = seeder;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await _directories.EnsureCreatedAsync(cancellationToken);
        await _migrationRunner.InitializeAsync(cancellationToken);
        await _seeder.SeedAsync(cancellationToken);
    }
}
```

Update `src/NovelSpeaker.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Infrastructure.FileSystem;
using NovelSpeaker.Infrastructure.Persistence;

namespace NovelSpeaker.Infrastructure.DependencyInjection;

/// <summary>
/// Registers infrastructure services required for application startup.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddNovelSpeakerInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IAppDataDirectoryProvider, LocalAppDataDirectoryProvider>();
        services.AddSingleton<ISqliteConnectionFactory, SqliteConnectionFactory>();
        services.AddSingleton<SqliteConnectionFactory>(provider => (SqliteConnectionFactory)provider.GetRequiredService<ISqliteConnectionFactory>());
        services.AddSingleton<SqliteMigrationRunner>();
        services.AddSingleton<ChapterRuleRepository>();
        services.AddSingleton<IChapterRuleRepository>(provider => provider.GetRequiredService<ChapterRuleRepository>());
        services.AddSingleton<DefaultChapterRuleSeeder>();
        services.AddSingleton<IDatabaseInitializer, StartupDatabaseInitializer>();

        return services;
    }
}
```

- [ ] **Step 4: Run the repository test to verify it passes**

Run:

```bash
dotnet test tests/NovelSpeaker.UnitTests/NovelSpeaker.UnitTests.csproj --filter ImportDefaultsAsync_skips_exact_duplicates_and_preserves_existing_rows
```

Expected: PASS. Re-importing defaults should add missing hard-coded rules without overwriting or re-enabling existing rows.

- [ ] **Step 5: Commit**

```bash
git add src/NovelSpeaker.Application/Books/IChapterRuleRepository.cs src/NovelSpeaker.Infrastructure/Books/DefaultChapterRules.cs src/NovelSpeaker.Infrastructure/Persistence/ChapterRuleRepository.cs src/NovelSpeaker.Infrastructure/Persistence/DefaultChapterRuleSeeder.cs src/NovelSpeaker.Infrastructure/Persistence/StartupDatabaseInitializer.cs src/NovelSpeaker.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs tests/NovelSpeaker.UnitTests/Books/ChapterRuleRepositoryTests.cs
git commit -m "feat(import): add chapter rule repository and default seeding"
```

### Task 3: Add encoding detection, preview extraction, normalization, and hashing

**Files:**
- Create: `src/NovelSpeaker.Application/Books/IContentHasher.cs`
- Create: `src/NovelSpeaker.Application/Books/ITextFileAnalyzer.cs`
- Create: `src/NovelSpeaker.Application/Books/ITextNormalizer.cs`
- Create: `src/NovelSpeaker.Application/Books/TextFileAnalysis.cs`
- Create: `src/NovelSpeaker.Infrastructure/Books/Text/Sha256ContentHasher.cs`
- Create: `src/NovelSpeaker.Infrastructure/Books/Text/TextFileAnalyzer.cs`
- Create: `src/NovelSpeaker.Infrastructure/Books/Text/TextNormalizer.cs`
- Modify: `src/NovelSpeaker.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs`
- Test: `tests/NovelSpeaker.UnitTests/Books/Sha256ContentHasherTests.cs`
- Test: `tests/NovelSpeaker.UnitTests/Books/TextFileAnalyzerTests.cs`
- Test: `tests/NovelSpeaker.UnitTests/Books/TextNormalizerTests.cs`

- [ ] **Step 1: Write the failing analysis tests**

Create `tests/NovelSpeaker.UnitTests/Books/TextFileAnalyzerTests.cs`:

```csharp
using System.Text;
using NovelSpeaker.Infrastructure.Books.Text;

namespace NovelSpeaker.UnitTests.Books;

public sealed class TextFileAnalyzerTests
{
    [Fact]
    public async Task AnalyzeAsync_reads_utf8_file_and_returns_preview()
    {
        var filePath = Path.GetTempFileName();
        await File.WriteAllTextAsync(filePath, "第一章 开始\n正文一\n正文二", new UTF8Encoding(false));

        var analyzer = new TextFileAnalyzer();
        var result = await analyzer.AnalyzeAsync(filePath, encodingName: null, CancellationToken.None);

        Assert.Equal("utf-8", result.EncodingName);
        Assert.Contains("第一章 开始", result.PreviewText);
        Assert.Contains("正文一", result.RawText);
    }

    [Fact]
    public async Task AnalyzeAsync_falls_back_to_gb18030_when_strict_utf8_fails()
    {
        var filePath = Path.GetTempFileName();
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var gb18030 = Encoding.GetEncoding("GB18030");
        await File.WriteAllTextAsync(filePath, "第一章 回退\n正文", gb18030);

        var analyzer = new TextFileAnalyzer();
        var result = await analyzer.AnalyzeAsync(filePath, encodingName: null, CancellationToken.None);

        Assert.Equal("gb18030", result.EncodingName);
        Assert.Contains("第一章 回退", result.RawText);
    }
}
```

Create `tests/NovelSpeaker.UnitTests/Books/TextNormalizerTests.cs`:

```csharp
using NovelSpeaker.Infrastructure.Books.Text;

namespace NovelSpeaker.UnitTests.Books;

public sealed class TextNormalizerTests
{
    [Fact]
    public void Normalize_converts_newlines_and_removes_control_characters()
    {
        var normalizer = new TextNormalizer();
        var result = normalizer.Normalize("第一章\r\n正文\u0001\r第二行\n");

        Assert.Equal("第一章\n正文\n第二行\n", result);
    }
}
```

Create `tests/NovelSpeaker.UnitTests/Books/Sha256ContentHasherTests.cs`:

```csharp
using NovelSpeaker.Infrastructure.Books.Text;

namespace NovelSpeaker.UnitTests.Books;

public sealed class Sha256ContentHasherTests
{
    [Fact]
    public async Task ComputeFileHashAsync_returns_stable_hex_hash()
    {
        var filePath = Path.GetTempFileName();
        await File.WriteAllTextAsync(filePath, "hash me");

        var hasher = new Sha256ContentHasher();
        var hash = await hasher.ComputeFileHashAsync(filePath, CancellationToken.None);

        Assert.Equal("eb201af5a3bcad53a1e92d8c4d8d9d1c66d003195f6668c9b9d0f8f38be7d6a0", hash);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run:

```bash
dotnet test tests/NovelSpeaker.UnitTests/NovelSpeaker.UnitTests.csproj --filter "AnalyzeAsync_reads_utf8_file_and_returns_preview|AnalyzeAsync_falls_back_to_gb18030_when_strict_utf8_fails|Normalize_converts_newlines_and_removes_control_characters|ComputeFileHashAsync_returns_stable_hex_hash"
```

Expected: FAIL because the analyzer, normalizer, and hasher types do not exist yet.

- [ ] **Step 3: Implement the analysis primitives**

Create `src/NovelSpeaker.Application/Books/TextFileAnalysis.cs`:

```csharp
namespace NovelSpeaker.Application.Books;

/// <summary>
/// Carries the decoded text and preview snippet from a TXT file.
/// </summary>
public sealed record TextFileAnalysis(
    string EncodingName,
    string PreviewText,
    string RawText);
```

Create `src/NovelSpeaker.Application/Books/ITextFileAnalyzer.cs`:

```csharp
namespace NovelSpeaker.Application.Books;

/// <summary>
/// Reads TXT files with automatic encoding detection and preview generation.
/// </summary>
public interface ITextFileAnalyzer
{
    Task<TextFileAnalysis> AnalyzeAsync(
        string filePath,
        string? encodingName,
        CancellationToken cancellationToken);
}
```

Create `src/NovelSpeaker.Application/Books/ITextNormalizer.cs`:

```csharp
namespace NovelSpeaker.Application.Books;

/// <summary>
/// Normalizes imported TXT content before chapter splitting.
/// </summary>
public interface ITextNormalizer
{
    string Normalize(string rawText);
}
```

Create `src/NovelSpeaker.Application/Books/IContentHasher.cs`:

```csharp
namespace NovelSpeaker.Application.Books;

/// <summary>
/// Computes deterministic content hashes used for duplicate detection.
/// </summary>
public interface IContentHasher
{
    Task<string> ComputeFileHashAsync(string filePath, CancellationToken cancellationToken);
}
```

Create `src/NovelSpeaker.Infrastructure/Books/Text/TextFileAnalyzer.cs`:

```csharp
using System.Text;
using NovelSpeaker.Application.Books;

namespace NovelSpeaker.Infrastructure.Books.Text;

/// <summary>
/// Detects BOM, strict UTF-8, and GB18030 for TXT files.
/// </summary>
public sealed class TextFileAnalyzer : ITextFileAnalyzer
{
    public TextFileAnalyzer()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public async Task<TextFileAnalysis> AnalyzeAsync(
        string filePath,
        string? encodingName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!string.IsNullOrWhiteSpace(encodingName))
        {
            var specified = GetEncoding(encodingName);
            var specifiedText = await File.ReadAllTextAsync(filePath, specified, cancellationToken);
            return new TextFileAnalysis(
                encodingName.ToLowerInvariant(),
                specifiedText[..Math.Min(specifiedText.Length, 800)],
                specifiedText);
        }

        try
        {
            var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            var utf8Text = await File.ReadAllTextAsync(filePath, utf8, cancellationToken);
            return new TextFileAnalysis(
                "utf-8",
                utf8Text[..Math.Min(utf8Text.Length, 800)],
                utf8Text);
        }
        catch (DecoderFallbackException)
        {
            var gb18030 = Encoding.GetEncoding("GB18030", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
            var gbText = await File.ReadAllTextAsync(filePath, gb18030, cancellationToken);
            return new TextFileAnalysis(
                "gb18030",
                gbText[..Math.Min(gbText.Length, 800)],
                gbText);
        }
    }

    private static Encoding GetEncoding(string encodingName) =>
        encodingName.ToLowerInvariant() switch
        {
            "utf-8" => new UTF8Encoding(false, true),
            "gb18030" => Encoding.GetEncoding("GB18030", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback),
            _ => throw new NotSupportedException($"Unsupported encoding: {encodingName}")
        };
}
```

Create `src/NovelSpeaker.Infrastructure/Books/Text/TextNormalizer.cs`:

```csharp
using System.Text;
using NovelSpeaker.Application.Books;

namespace NovelSpeaker.Infrastructure.Books.Text;

/// <summary>
/// Normalizes newlines and removes unsupported control characters.
/// </summary>
public sealed class TextNormalizer : ITextNormalizer
{
    public string Normalize(string rawText)
    {
        var unified = rawText.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

        var builder = new StringBuilder(unified.Length);
        foreach (var character in unified)
        {
            if (character == '\n' || character == '\t' || !char.IsControl(character))
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }
}
```

Create `src/NovelSpeaker.Infrastructure/Books/Text/Sha256ContentHasher.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;
using NovelSpeaker.Application.Books;

namespace NovelSpeaker.Infrastructure.Books.Text;

/// <summary>
/// Computes lowercase SHA-256 hashes for imported source files.
/// </summary>
public sealed class Sha256ContentHasher : IContentHasher
{
    public async Task<string> ComputeFileHashAsync(string filePath, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(filePath);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);

        var builder = new StringBuilder(hash.Length * 2);
        foreach (var item in hash)
        {
            builder.Append(item.ToString("x2"));
        }

        return builder.ToString();
    }
}
```

Update `src/NovelSpeaker.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs` to add:

```csharp
services.AddSingleton<ITextFileAnalyzer, TextFileAnalyzer>();
services.AddSingleton<ITextNormalizer, TextNormalizer>();
services.AddSingleton<IContentHasher, Sha256ContentHasher>();
```

- [ ] **Step 4: Run the analysis tests to verify they pass**

Run:

```bash
dotnet test tests/NovelSpeaker.UnitTests/NovelSpeaker.UnitTests.csproj --filter "AnalyzeAsync_reads_utf8_file_and_returns_preview|AnalyzeAsync_falls_back_to_gb18030_when_strict_utf8_fails|Normalize_converts_newlines_and_removes_control_characters|ComputeFileHashAsync_returns_stable_hex_hash"
```

Expected: PASS. The analyzer should read UTF-8 and GB18030 files, the normalizer should produce canonical `\n` newlines, and the hasher should return stable lowercase SHA-256 text.

- [ ] **Step 5: Commit**

```bash
git add src/NovelSpeaker.Application/Books/IContentHasher.cs src/NovelSpeaker.Application/Books/ITextFileAnalyzer.cs src/NovelSpeaker.Application/Books/ITextNormalizer.cs src/NovelSpeaker.Application/Books/TextFileAnalysis.cs src/NovelSpeaker.Infrastructure/Books/Text src/NovelSpeaker.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs tests/NovelSpeaker.UnitTests/Books/Sha256ContentHasherTests.cs tests/NovelSpeaker.UnitTests/Books/TextFileAnalyzerTests.cs tests/NovelSpeaker.UnitTests/Books/TextNormalizerTests.cs
git commit -m "feat(import): add text analysis primitives"
```

### Task 4: Add rule-driven chapter splitting

**Files:**
- Create: `src/NovelSpeaker.Application/Books/BookImportChapter.cs`
- Create: `src/NovelSpeaker.Application/Books/IChapterSplitter.cs`
- Create: `src/NovelSpeaker.Infrastructure/Books/Parsing/ChapterSplitter.cs`
- Modify: `src/NovelSpeaker.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs`
- Test: `tests/NovelSpeaker.UnitTests/Books/ChapterSplitterTests.cs`

- [ ] **Step 1: Write the failing splitter tests**

Create `tests/NovelSpeaker.UnitTests/Books/ChapterSplitterTests.cs`:

```csharp
using NovelSpeaker.Domain.Books;
using NovelSpeaker.Infrastructure.Books.Parsing;

namespace NovelSpeaker.UnitTests.Books;

public sealed class ChapterSplitterTests
{
    [Fact]
    public void Split_returns_ordered_chapters_with_offsets()
    {
        var rules =
        [
            new ChapterRule("1", "章节", @"^\s*第[0-9一二三四五六七八九十百千零两]+章(?:\s+.+)?\s*$", 10, true, "now", "now")
        ];

        var text = "第一章 开始\n正文甲\n第二章 继续\n正文乙\n";
        var splitter = new ChapterSplitter();

        var chapters = splitter.Split(text, rules);

        Assert.Equal(2, chapters.Count);
        Assert.Equal("第一章 开始", chapters[0].Title);
        Assert.Equal("正文甲\n", chapters[0].Content);
        Assert.Equal(6, chapters[0].StartOffset);
        Assert.Equal("第二章 继续", chapters[1].Title);
    }

    [Fact]
    public void Split_returns_empty_when_no_non_blank_chapter_content_exists()
    {
        var rules =
        [
            new ChapterRule("1", "章节", @"^\s*第[0-9一二三四五六七八九十百千零两]+章(?:\s+.+)?\s*$", 10, true, "now", "now")
        ];

        var text = "第一章 开始\n\n第二章 继续\n\n";
        var splitter = new ChapterSplitter();

        var chapters = splitter.Split(text, rules);

        Assert.Empty(chapters);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run:

```bash
dotnet test tests/NovelSpeaker.UnitTests/NovelSpeaker.UnitTests.csproj --filter "Split_returns_ordered_chapters_with_offsets|Split_returns_empty_when_no_non_blank_chapter_content_exists"
```

Expected: FAIL because `IChapterSplitter`, `BookImportChapter`, and `ChapterSplitter` do not exist yet.

- [ ] **Step 3: Implement the splitter**

Create `src/NovelSpeaker.Application/Books/BookImportChapter.cs`:

```csharp
namespace NovelSpeaker.Application.Books;

/// <summary>
/// Represents one chapter found during import analysis before database IDs exist.
/// </summary>
public sealed record BookImportChapter(
    int ChapterIndex,
    string Title,
    string Content,
    int StartOffset,
    int Length);
```

Create `src/NovelSpeaker.Application/Books/IChapterSplitter.cs`:

```csharp
using NovelSpeaker.Domain.Books;

namespace NovelSpeaker.Application.Books;

/// <summary>
/// Splits normalized TXT content into chapters using enabled database rules.
/// </summary>
public interface IChapterSplitter
{
    IReadOnlyList<BookImportChapter> Split(
        string normalizedText,
        IReadOnlyList<ChapterRule> rules);
}
```

Create `src/NovelSpeaker.Infrastructure/Books/Parsing/ChapterSplitter.cs`:

```csharp
using System.Text.RegularExpressions;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Domain.Books;

namespace NovelSpeaker.Infrastructure.Books.Parsing;

/// <summary>
/// Matches title lines with ordered rules and builds persisted chapter ranges.
/// </summary>
public sealed class ChapterSplitter : IChapterSplitter
{
    public IReadOnlyList<BookImportChapter> Split(string normalizedText, IReadOnlyList<ChapterRule> rules)
    {
        if (string.IsNullOrWhiteSpace(normalizedText) || rules.Count == 0)
        {
            return [];
        }

        var markers = new List<(int TitleOffset, int ContentOffset, string Title)>();
        var orderedRules = rules.Where(rule => rule.IsEnabled).OrderBy(rule => rule.SortOrder).ToArray();
        var lineStart = 0;

        foreach (var line in normalizedText.Split('\n'))
        {
            var matchedRule = orderedRules.FirstOrDefault(rule => Regex.IsMatch(line, rule.Pattern, RegexOptions.CultureInvariant));
            if (matchedRule is not null)
            {
                markers.Add((lineStart, lineStart + line.Length + 1, line.Trim()));
            }

            lineStart += line.Length + 1;
        }

        if (markers.Count == 0)
        {
            return [];
        }

        var chapters = new List<BookImportChapter>();
        for (var index = 0; index < markers.Count; index++)
        {
            var current = markers[index];
            var nextTitleOffset = index + 1 < markers.Count ? markers[index + 1].TitleOffset : normalizedText.Length;
            var contentLength = nextTitleOffset - current.ContentOffset;
            if (contentLength <= 0)
            {
                continue;
            }

            var content = normalizedText.Substring(current.ContentOffset, contentLength);
            if (string.IsNullOrWhiteSpace(content))
            {
                continue;
            }

            chapters.Add(new BookImportChapter(
                chapters.Count,
                current.Title,
                content,
                current.ContentOffset,
                content.Length));
        }

        return chapters;
    }
}
```

Update `src/NovelSpeaker.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs` to add:

```csharp
services.AddSingleton<IChapterSplitter, ChapterSplitter>();
```

- [ ] **Step 4: Run the splitter tests to verify they pass**

Run:

```bash
dotnet test tests/NovelSpeaker.UnitTests/NovelSpeaker.UnitTests.csproj --filter "Split_returns_ordered_chapters_with_offsets|Split_returns_empty_when_no_non_blank_chapter_content_exists"
```

Expected: PASS. Offsets should point into normalized text, and chapterless or empty-content results should come back as an empty list.

- [ ] **Step 5: Commit**

```bash
git add src/NovelSpeaker.Application/Books/BookImportChapter.cs src/NovelSpeaker.Application/Books/IChapterSplitter.cs src/NovelSpeaker.Infrastructure/Books/Parsing/ChapterSplitter.cs src/NovelSpeaker.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs tests/NovelSpeaker.UnitTests/Books/ChapterSplitterTests.cs
git commit -m "feat(import): add rule-driven chapter splitter"
```

### Task 5: Add duplicate detection, book-file storage, transactional import persistence, and library queries

**Files:**
- Create: `src/NovelSpeaker.Application/Books/BookSummary.cs`
- Create: `src/NovelSpeaker.Application/Books/IBookCatalogService.cs`
- Create: `src/NovelSpeaker.Application/Books/IBookDuplicateDetector.cs`
- Create: `src/NovelSpeaker.Application/Books/BookFileCopyHandle.cs`
- Create: `src/NovelSpeaker.Application/Books/IBookFileStore.cs`
- Create: `src/NovelSpeaker.Application/Books/IBookImportRepository.cs`
- Create: `src/NovelSpeaker.Infrastructure/Books/FileStorage/BookFileStore.cs`
- Create: `src/NovelSpeaker.Infrastructure/Persistence/BookCatalogService.cs`
- Create: `src/NovelSpeaker.Infrastructure/Persistence/BookDuplicateDetector.cs`
- Create: `src/NovelSpeaker.Infrastructure/Persistence/BookImportRepository.cs`
- Modify: `src/NovelSpeaker.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs`
- Test: `tests/NovelSpeaker.UnitTests/Books/BookDuplicateDetectorTests.cs`
- Test: `tests/NovelSpeaker.UnitTests/Books/BookFileStoreTests.cs`
- Test: `tests/NovelSpeaker.UnitTests/Books/BookImportRepositoryTests.cs`

- [ ] **Step 1: Write the failing persistence tests**

Create `tests/NovelSpeaker.UnitTests/Books/BookImportRepositoryTests.cs`:

```csharp
using NovelSpeaker.Domain.Books;
using NovelSpeaker.Infrastructure.FileSystem;
using NovelSpeaker.Infrastructure.Persistence;

namespace NovelSpeaker.UnitTests.Books;

public sealed class BookImportRepositoryTests
{
    [Fact]
    public async Task SaveAsync_rolls_back_when_a_chapter_insert_breaks_the_unique_index()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var directories = new LocalAppDataDirectoryProvider(root);
        var factory = new SqliteConnectionFactory(directories);
        var runner = new SqliteMigrationRunner(factory);
        var initializer = new StartupDatabaseInitializer(directories, runner);
        await initializer.InitializeAsync(CancellationToken.None);

        var repository = new BookImportRepository(factory);
        var now = DateTime.UtcNow.ToString("O");
        var book = new Book("book-1", "书名", null, "demo.txt", "stored.txt", "hash-1", "utf-8", now, now);
        var chapters =
        [
            new Chapter("c-1", "book-1", 0, "第一章", "正文甲", 0, 3),
            new Chapter("c-2", "book-1", 0, "第二章", "正文乙", 10, 3)
        ];

        await Assert.ThrowsAsync<SqliteException>(() => repository.SaveAsync(book, chapters, CancellationToken.None));

        await using var connection = await factory.OpenConnectionAsync(CancellationToken.None);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Books;";
        var count = Convert.ToInt32(await command.ExecuteScalarAsync(CancellationToken.None));

        Assert.Equal(0, count);
    }
}
```

Create `tests/NovelSpeaker.UnitTests/Books/BookDuplicateDetectorTests.cs`:

```csharp
using NovelSpeaker.Domain.Books;
using NovelSpeaker.Infrastructure.FileSystem;
using NovelSpeaker.Infrastructure.Persistence;

namespace NovelSpeaker.UnitTests.Books;

public sealed class BookDuplicateDetectorTests
{
    [Fact]
    public async Task FindExistingBookIdAsync_returns_existing_id_for_matching_hash()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var directories = new LocalAppDataDirectoryProvider(root);
        var factory = new SqliteConnectionFactory(directories);
        var runner = new SqliteMigrationRunner(factory);
        var initializer = new StartupDatabaseInitializer(directories, runner);
        await initializer.InitializeAsync(CancellationToken.None);

        var repository = new BookImportRepository(factory);
        var detector = new BookDuplicateDetector(factory);
        var now = DateTime.UtcNow.ToString("O");
        var book = new Book("book-1", "书名", null, "demo.txt", "stored.txt", "hash-dup", "utf-8", now, now);
        var chapters = [new Chapter("c-1", "book-1", 0, "第一章", "正文甲", 0, 3)];

        await repository.SaveAsync(book, chapters, CancellationToken.None);

        var existingId = await detector.FindExistingBookIdAsync("hash-dup", CancellationToken.None);
        Assert.Equal("book-1", existingId);
    }
}
```

Create `tests/NovelSpeaker.UnitTests/Books/BookFileStoreTests.cs`:

```csharp
using NovelSpeaker.Infrastructure.Books.FileStorage;
using NovelSpeaker.Infrastructure.FileSystem;

namespace NovelSpeaker.UnitTests.Books;

public sealed class BookFileStoreTests
{
    [Fact]
    public async Task PrepareCopyAsync_and_finalizeAsync_create_original_txt_inside_book_directory()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var directories = new LocalAppDataDirectoryProvider(root);
        await directories.EnsureCreatedAsync(CancellationToken.None);

        var sourceFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(sourceFile, "测试正文");

        var store = new BookFileStore(directories);
        var handle = await store.PrepareCopyAsync(sourceFile, "book-1", CancellationToken.None);
        await store.FinalizeAsync(handle, CancellationToken.None);

        Assert.True(File.Exists(handle.FinalPath));
        Assert.False(File.Exists(handle.TemporaryPath));
        Assert.Equal(Path.Combine(directories.BooksDirectoryPath, "book-1", "original.txt"), handle.FinalPath);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run:

```bash
dotnet test tests/NovelSpeaker.UnitTests/NovelSpeaker.UnitTests.csproj --filter "SaveAsync_rolls_back_when_a_chapter_insert_breaks_the_unique_index|FindExistingBookIdAsync_returns_existing_id_for_matching_hash|PrepareCopyAsync_and_finalizeAsync_create_original_txt_inside_book_directory"
```

Expected: FAIL because the duplicate detector, file store, and import repository do not exist yet.

- [ ] **Step 3: Implement the persistence and file services**

Create `src/NovelSpeaker.Application/Books/BookSummary.cs`:

```csharp
namespace NovelSpeaker.Application.Books;

/// <summary>
/// Represents a minimal book row for the library page.
/// </summary>
public sealed record BookSummary(
    string Id,
    string Title,
    string? Author,
    string CurrentChapterTitle,
    string ImportedAt);
```

Create `src/NovelSpeaker.Application/Books/IBookDuplicateDetector.cs`:

```csharp
namespace NovelSpeaker.Application.Books;

/// <summary>
/// Looks up existing imported books by source hash.
/// </summary>
public interface IBookDuplicateDetector
{
    Task<string?> FindExistingBookIdAsync(string sourceHash, CancellationToken cancellationToken);
}
```

Create `src/NovelSpeaker.Application/Books/BookFileCopyHandle.cs`:

```csharp
namespace NovelSpeaker.Application.Books;

/// <summary>
/// Describes the temporary and final target paths for one copied source TXT file.
/// </summary>
public sealed record BookFileCopyHandle(
    string FinalPath,
    string TemporaryPath);
```

Create `src/NovelSpeaker.Application/Books/IBookFileStore.cs`:

```csharp
namespace NovelSpeaker.Application.Books;

/// <summary>
/// Copies imported source files into the application-owned book storage.
/// </summary>
public interface IBookFileStore
{
    Task<BookFileCopyHandle> PrepareCopyAsync(string sourceFilePath, string bookId, CancellationToken cancellationToken);
    Task FinalizeAsync(BookFileCopyHandle copyHandle, CancellationToken cancellationToken);
    Task CleanupAsync(BookFileCopyHandle copyHandle);
}
```

Create `src/NovelSpeaker.Application/Books/IBookImportRepository.cs`:

```csharp
using NovelSpeaker.Domain.Books;

namespace NovelSpeaker.Application.Books;

/// <summary>
/// Writes imported books and chapters in one SQLite transaction.
/// </summary>
public interface IBookImportRepository
{
    Task SaveAsync(Book book, IReadOnlyList<Chapter> chapters, CancellationToken cancellationToken);
}
```

Create `src/NovelSpeaker.Application/Books/IBookCatalogService.cs`:

```csharp
namespace NovelSpeaker.Application.Books;

/// <summary>
/// Loads lightweight library items for the book list page.
/// </summary>
public interface IBookCatalogService
{
    Task<IReadOnlyList<BookSummary>> GetBooksAsync(CancellationToken cancellationToken);
}
```

Create `src/NovelSpeaker.Infrastructure/Books/FileStorage/BookFileStore.cs`:

```csharp
using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Application.Books;

namespace NovelSpeaker.Infrastructure.Books.FileStorage;

/// <summary>
/// Writes imported source files through a temporary file and atomic move.
/// </summary>
public sealed class BookFileStore : IBookFileStore
{
    private readonly IAppDataDirectoryProvider _directories;

    public BookFileStore(IAppDataDirectoryProvider directories)
    {
        _directories = directories;
    }

    public async Task<BookFileCopyHandle> PrepareCopyAsync(string sourceFilePath, string bookId, CancellationToken cancellationToken)
    {
        var directory = Path.Combine(_directories.BooksDirectoryPath, bookId);
        Directory.CreateDirectory(directory);

        var finalPath = Path.Combine(directory, "original.txt");
        var temporaryPath = Path.Combine(directory, "original.txt.tmp");

        await using var source = File.OpenRead(sourceFilePath);
        await using var destination = File.Create(temporaryPath);
        await source.CopyToAsync(destination, cancellationToken);

        return new BookFileCopyHandle(finalPath, temporaryPath);
    }

    public Task FinalizeAsync(BookFileCopyHandle copyHandle, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        File.Move(copyHandle.TemporaryPath, copyHandle.FinalPath, overwrite: true);
        return Task.CompletedTask;
    }

    public Task CleanupAsync(BookFileCopyHandle copyHandle)
    {
        if (File.Exists(copyHandle.TemporaryPath))
        {
            File.Delete(copyHandle.TemporaryPath);
        }

        return Task.CompletedTask;
    }
}
```

Create `src/NovelSpeaker.Infrastructure/Persistence/BookDuplicateDetector.cs`:

```csharp
using NovelSpeaker.Application.Books;

namespace NovelSpeaker.Infrastructure.Persistence;

/// <summary>
/// Checks for already-imported books using their source hash.
/// </summary>
public sealed class BookDuplicateDetector : IBookDuplicateDetector
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public BookDuplicateDetector(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<string?> FindExistingBookIdAsync(string sourceHash, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT Id FROM Books WHERE SourceHash = $sourceHash LIMIT 1;";
        command.Parameters.AddWithValue("$sourceHash", sourceHash);
        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }
}
```

Create `src/NovelSpeaker.Infrastructure/Persistence/BookImportRepository.cs`:

```csharp
using NovelSpeaker.Application.Books;
using NovelSpeaker.Domain.Books;

namespace NovelSpeaker.Infrastructure.Persistence;

/// <summary>
/// Saves imported books and chapters inside a single SQLite transaction.
/// </summary>
public sealed class BookImportRepository : IBookImportRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public BookImportRepository(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task SaveAsync(Book book, IReadOnlyList<Chapter> chapters, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var bookCommand = connection.CreateCommand();
            bookCommand.Transaction = transaction;
            bookCommand.CommandText =
                """
                INSERT INTO Books (Id, Title, Author, OriginalFileName, StoredFilePath, SourceHash, Encoding, ImportedAt, UpdatedAt)
                VALUES ($id, $title, $author, $originalFileName, $storedFilePath, $sourceHash, $encoding, $importedAt, $updatedAt);
                """;
            bookCommand.Parameters.AddWithValue("$id", book.Id);
            bookCommand.Parameters.AddWithValue("$title", book.Title);
            bookCommand.Parameters.AddWithValue("$author", (object?)book.Author ?? DBNull.Value);
            bookCommand.Parameters.AddWithValue("$originalFileName", book.OriginalFileName);
            bookCommand.Parameters.AddWithValue("$storedFilePath", book.StoredFilePath);
            bookCommand.Parameters.AddWithValue("$sourceHash", book.SourceHash);
            bookCommand.Parameters.AddWithValue("$encoding", book.Encoding);
            bookCommand.Parameters.AddWithValue("$importedAt", book.ImportedAt);
            bookCommand.Parameters.AddWithValue("$updatedAt", book.UpdatedAt);
            await bookCommand.ExecuteNonQueryAsync(cancellationToken);

            foreach (var chapter in chapters)
            {
                var chapterCommand = connection.CreateCommand();
                chapterCommand.Transaction = transaction;
                chapterCommand.CommandText =
                    """
                    INSERT INTO Chapters (Id, BookId, ChapterIndex, Title, Content, StartOffset, Length)
                    VALUES ($id, $bookId, $chapterIndex, $title, $content, $startOffset, $length);
                    """;
                chapterCommand.Parameters.AddWithValue("$id", chapter.Id);
                chapterCommand.Parameters.AddWithValue("$bookId", chapter.BookId);
                chapterCommand.Parameters.AddWithValue("$chapterIndex", chapter.ChapterIndex);
                chapterCommand.Parameters.AddWithValue("$title", chapter.Title);
                chapterCommand.Parameters.AddWithValue("$content", chapter.Content);
                chapterCommand.Parameters.AddWithValue("$startOffset", chapter.StartOffset);
                chapterCommand.Parameters.AddWithValue("$length", chapter.Length);
                await chapterCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
```

Create `src/NovelSpeaker.Infrastructure/Persistence/BookCatalogService.cs`:

```csharp
using NovelSpeaker.Application.Books;

namespace NovelSpeaker.Infrastructure.Persistence;

/// <summary>
/// Reads lightweight book rows for the library page.
/// </summary>
public sealed class BookCatalogService : IBookCatalogService
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public BookCatalogService(SqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyList<BookSummary>> GetBooksAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT b.Id,
                   b.Title,
                   b.Author,
                   COALESCE(
                       (SELECT Title
                        FROM Chapters c
                        WHERE c.BookId = b.Id
                        ORDER BY c.ChapterIndex
                        LIMIT 1),
                       '未开始')
                   AS CurrentChapterTitle,
                   b.ImportedAt
            FROM Books b
            ORDER BY b.ImportedAt DESC;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var books = new List<BookSummary>();

        while (await reader.ReadAsync(cancellationToken))
        {
            books.Add(new BookSummary(
                reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4)));
        }

        return books;
    }
}
```

Update `src/NovelSpeaker.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs` to add:

```csharp
services.AddSingleton<IBookDuplicateDetector, BookDuplicateDetector>();
services.AddSingleton<IBookImportRepository, BookImportRepository>();
services.AddSingleton<IBookCatalogService, BookCatalogService>();
services.AddSingleton<IBookFileStore, BookFileStore>();
```

- [ ] **Step 4: Run the persistence tests to verify they pass**

Run:

```bash
dotnet test tests/NovelSpeaker.UnitTests/NovelSpeaker.UnitTests.csproj --filter "SaveAsync_rolls_back_when_a_chapter_insert_breaks_the_unique_index|FindExistingBookIdAsync_returns_existing_id_for_matching_hash|PrepareCopyAsync_and_finalizeAsync_create_original_txt_inside_book_directory"
```

Expected: PASS. Book writes should roll back on chapter failure, duplicate lookup should find an existing hash, and original TXT copies should land at `Books/<book-id>/original.txt`.

- [ ] **Step 5: Commit**

```bash
git add src/NovelSpeaker.Application/Books/BookSummary.cs src/NovelSpeaker.Application/Books/IBookCatalogService.cs src/NovelSpeaker.Application/Books/IBookDuplicateDetector.cs src/NovelSpeaker.Application/Books/BookFileCopyHandle.cs src/NovelSpeaker.Application/Books/IBookFileStore.cs src/NovelSpeaker.Application/Books/IBookImportRepository.cs src/NovelSpeaker.Infrastructure/Books/FileStorage src/NovelSpeaker.Infrastructure/Persistence/BookCatalogService.cs src/NovelSpeaker.Infrastructure/Persistence/BookDuplicateDetector.cs src/NovelSpeaker.Infrastructure/Persistence/BookImportRepository.cs src/NovelSpeaker.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs tests/NovelSpeaker.UnitTests/Books/BookDuplicateDetectorTests.cs tests/NovelSpeaker.UnitTests/Books/BookFileStoreTests.cs tests/NovelSpeaker.UnitTests/Books/BookImportRepositoryTests.cs
git commit -m "feat(import): add transactional persistence and file storage"
```

### Task 6: Add the two-phase `IBookImportService`

**Files:**
- Create: `src/NovelSpeaker.Application/Books/BookImportAnalysis.cs`
- Create: `src/NovelSpeaker.Application/Books/BookImportAnalysisStatus.cs`
- Create: `src/NovelSpeaker.Application/Books/BookImportFailureReason.cs`
- Create: `src/NovelSpeaker.Application/Books/BookImportResult.cs`
- Create: `src/NovelSpeaker.Application/Books/IBookImportService.cs`
- Create: `src/NovelSpeaker.Infrastructure/Books/BookImportService.cs`
- Modify: `src/NovelSpeaker.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs`
- Test: `tests/NovelSpeaker.UnitTests/Books/BookImportServiceTests.cs`

- [ ] **Step 1: Write the failing service tests**

Create `tests/NovelSpeaker.UnitTests/Books/BookImportServiceTests.cs`:

```csharp
using NovelSpeaker.Application.Books;
using NovelSpeaker.Domain.Books;
using NovelSpeaker.Infrastructure.Books;

namespace NovelSpeaker.UnitTests.Books;

public sealed class BookImportServiceTests
{
    [Fact]
    public async Task AnalyzeAsync_returns_duplicate_failure_when_hash_already_exists()
    {
        var analyzer = new FakeTextFileAnalyzer(new TextFileAnalysis("utf-8", "preview", "第一章 开始\n正文"));
        var normalizer = new FakeTextNormalizer("第一章 开始\n正文");
        var hasher = new FakeContentHasher("same-hash");
        var duplicates = new FakeDuplicateDetector("book-42");
        var rules = new FakeChapterRuleRepository([
            new ChapterRule("rule-1", "章节", @"^\s*第[0-9一二三四五六七八九十百千零两]+章(?:\s+.+)?\s*$", 10, true, "now", "now")
        ]);
        var splitter = new FakeChapterSplitter([new BookImportChapter(0, "第一章 开始", "正文", 6, 2)]);
        var service = new BookImportService(analyzer, normalizer, hasher, duplicates, rules, splitter, new FakeBookFileStore(), new FakeBookImportRepository());

        var analysis = await service.AnalyzeAsync("demo.txt", null, CancellationToken.None);

        Assert.Equal(BookImportAnalysisStatus.Failed, analysis.Status);
        Assert.Equal(BookImportFailureReason.DuplicateBook, analysis.FailureReason);
        Assert.Equal("book-42", analysis.ExistingBookId);
    }

    [Fact]
    public async Task CommitAsync_throws_when_analysis_is_not_ready()
    {
        var service = new BookImportService(
            new FakeTextFileAnalyzer(new TextFileAnalysis("utf-8", "preview", "第一章 开始\n正文")),
            new FakeTextNormalizer("第一章 开始\n正文"),
            new FakeContentHasher("hash"),
            new FakeDuplicateDetector(null),
            new FakeChapterRuleRepository([]),
            new FakeChapterSplitter([]),
            new FakeBookFileStore(),
            new FakeBookImportRepository());

        var analysis = new BookImportAnalysis(
            BookImportAnalysisStatus.Failed,
            "demo.txt",
            "demo.txt",
            "demo",
            "utf-8",
            "preview",
            "正文",
            "hash",
            [],
            BookImportFailureReason.NoValidChapters,
            null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CommitAsync(analysis, CancellationToken.None));
    }

    private sealed class FakeTextFileAnalyzer : ITextFileAnalyzer
    {
        private readonly TextFileAnalysis _result;

        public FakeTextFileAnalyzer(TextFileAnalysis result)
        {
            _result = result;
        }

        public Task<TextFileAnalysis> AnalyzeAsync(string filePath, string? encodingName, CancellationToken cancellationToken)
        {
            return Task.FromResult(_result);
        }
    }

    private sealed class FakeTextNormalizer : ITextNormalizer
    {
        private readonly string _normalizedText;

        public FakeTextNormalizer(string normalizedText)
        {
            _normalizedText = normalizedText;
        }

        public string Normalize(string rawText) => _normalizedText;
    }

    private sealed class FakeContentHasher : IContentHasher
    {
        private readonly string _hash;

        public FakeContentHasher(string hash)
        {
            _hash = hash;
        }

        public Task<string> ComputeFileHashAsync(string filePath, CancellationToken cancellationToken)
        {
            return Task.FromResult(_hash);
        }
    }

    private sealed class FakeDuplicateDetector : IBookDuplicateDetector
    {
        private readonly string? _existingId;

        public FakeDuplicateDetector(string? existingId)
        {
            _existingId = existingId;
        }

        public Task<string?> FindExistingBookIdAsync(string sourceHash, CancellationToken cancellationToken)
        {
            return Task.FromResult(_existingId);
        }
    }

    private sealed class FakeChapterRuleRepository : IChapterRuleRepository
    {
        private readonly IReadOnlyList<ChapterRule> _rules;

        public FakeChapterRuleRepository(IReadOnlyList<ChapterRule> rules)
        {
            _rules = rules;
        }

        public Task<IReadOnlyList<ChapterRule>> GetAllAsync(CancellationToken cancellationToken) => Task.FromResult(_rules);
        public Task<IReadOnlyList<ChapterRule>> GetEnabledAsync(CancellationToken cancellationToken) => Task.FromResult(_rules);
        public Task SaveAsync(ChapterRule rule, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task DeleteAsync(string ruleId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task MoveAsync(string ruleId, int newSortOrder, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<int> ImportDefaultsAsync(CancellationToken cancellationToken) => Task.FromResult(0);
    }

    private sealed class FakeChapterSplitter : IChapterSplitter
    {
        private readonly IReadOnlyList<BookImportChapter> _chapters;

        public FakeChapterSplitter(IReadOnlyList<BookImportChapter> chapters)
        {
            _chapters = chapters;
        }

        public IReadOnlyList<BookImportChapter> Split(string normalizedText, IReadOnlyList<ChapterRule> rules) => _chapters;
    }

    private sealed class FakeBookFileStore : IBookFileStore
    {
        public Task<BookFileCopyHandle> PrepareCopyAsync(string sourceFilePath, string bookId, CancellationToken cancellationToken)
        {
            return Task.FromResult(new BookFileCopyHandle($"Books/{bookId}/original.txt", $"Books/{bookId}/original.txt.tmp"));
        }

        public Task FinalizeAsync(BookFileCopyHandle copyHandle, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task CleanupAsync(BookFileCopyHandle copyHandle) => Task.CompletedTask;
    }

    private sealed class FakeBookImportRepository : IBookImportRepository
    {
        public Task SaveAsync(Book book, IReadOnlyList<Chapter> chapters, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run:

```bash
dotnet test tests/NovelSpeaker.UnitTests/NovelSpeaker.UnitTests.csproj --filter "AnalyzeAsync_returns_duplicate_failure_when_hash_already_exists|CommitAsync_throws_when_analysis_is_not_ready"
```

Expected: FAIL because `BookImportService`, `BookImportAnalysis`, and related result types do not exist yet.

- [ ] **Step 3: Implement the application result types and orchestration**

Create `src/NovelSpeaker.Application/Books/BookImportAnalysisStatus.cs`:

```csharp
namespace NovelSpeaker.Application.Books;

public enum BookImportAnalysisStatus
{
    ReadyToCommit,
    Failed
}
```

Create `src/NovelSpeaker.Application/Books/BookImportFailureReason.cs`:

```csharp
namespace NovelSpeaker.Application.Books;

public enum BookImportFailureReason
{
    UnsupportedEncoding,
    DuplicateBook,
    NoValidChapters,
    FileReadFailed,
    TextNormalizationFailed
}
```

Create `src/NovelSpeaker.Application/Books/BookImportAnalysis.cs`:

```csharp
namespace NovelSpeaker.Application.Books;

/// <summary>
/// Holds the analysis outcome before the caller decides whether to commit it.
/// </summary>
public sealed record BookImportAnalysis(
    BookImportAnalysisStatus Status,
    string OriginalFilePath,
    string OriginalFileName,
    string SuggestedTitle,
    string DetectedEncoding,
    string PreviewText,
    string NormalizedText,
    string SourceHash,
    IReadOnlyList<BookImportChapter> Chapters,
    BookImportFailureReason? FailureReason,
    string? ExistingBookId);
```

Create `src/NovelSpeaker.Application/Books/BookImportResult.cs`:

```csharp
namespace NovelSpeaker.Application.Books;

/// <summary>
/// Reports the persisted book identity after a successful import.
/// </summary>
public sealed record BookImportResult(
    string BookId,
    string Title,
    int ChapterCount);
```

Create `src/NovelSpeaker.Application/Books/IBookImportService.cs`:

```csharp
namespace NovelSpeaker.Application.Books;

/// <summary>
/// Coordinates TXT analysis and transactional import commit for the UI layer.
/// </summary>
public interface IBookImportService
{
    Task<BookImportAnalysis> AnalyzeAsync(
        string filePath,
        string? encodingName,
        CancellationToken cancellationToken);

    Task<BookImportResult> CommitAsync(
        BookImportAnalysis analysis,
        CancellationToken cancellationToken);
}
```

Create `src/NovelSpeaker.Infrastructure/Books/BookImportService.cs`:

```csharp
using NovelSpeaker.Application.Books;
using NovelSpeaker.Domain.Books;
using NovelSpeaker.Infrastructure.Books.FileStorage;

namespace NovelSpeaker.Infrastructure.Books;

/// <summary>
/// Runs the analyze-then-commit import workflow.
/// </summary>
public sealed class BookImportService : IBookImportService
{
    private readonly ITextFileAnalyzer _textFileAnalyzer;
    private readonly ITextNormalizer _textNormalizer;
    private readonly IContentHasher _contentHasher;
    private readonly IBookDuplicateDetector _duplicateDetector;
    private readonly IChapterRuleRepository _chapterRuleRepository;
    private readonly IChapterSplitter _chapterSplitter;
    private readonly IBookFileStore _bookFileStore;
    private readonly IBookImportRepository _bookImportRepository;

    public BookImportService(
        ITextFileAnalyzer textFileAnalyzer,
        ITextNormalizer textNormalizer,
        IContentHasher contentHasher,
        IBookDuplicateDetector duplicateDetector,
        IChapterRuleRepository chapterRuleRepository,
        IChapterSplitter chapterSplitter,
        IBookFileStore bookFileStore,
        IBookImportRepository bookImportRepository)
    {
        _textFileAnalyzer = textFileAnalyzer;
        _textNormalizer = textNormalizer;
        _contentHasher = contentHasher;
        _duplicateDetector = duplicateDetector;
        _chapterRuleRepository = chapterRuleRepository;
        _chapterSplitter = chapterSplitter;
        _bookFileStore = bookFileStore;
        _bookImportRepository = bookImportRepository;
    }

    public async Task<BookImportAnalysis> AnalyzeAsync(
        string filePath,
        string? encodingName,
        CancellationToken cancellationToken)
    {
        try
        {
            var analyzedText = await _textFileAnalyzer.AnalyzeAsync(filePath, encodingName, cancellationToken);
            var normalizedText = _textNormalizer.Normalize(analyzedText.RawText);
            var sourceHash = await _contentHasher.ComputeFileHashAsync(filePath, cancellationToken);
            var existingBookId = await _duplicateDetector.FindExistingBookIdAsync(sourceHash, cancellationToken);

            if (existingBookId is not null)
            {
                return new BookImportAnalysis(
                    BookImportAnalysisStatus.Failed,
                    filePath,
                    Path.GetFileName(filePath),
                    Path.GetFileNameWithoutExtension(filePath),
                    analyzedText.EncodingName,
                    analyzedText.PreviewText,
                    normalizedText,
                    sourceHash,
                    [],
                    BookImportFailureReason.DuplicateBook,
                    existingBookId);
            }

            var rules = await _chapterRuleRepository.GetEnabledAsync(cancellationToken);
            var chapters = _chapterSplitter.Split(normalizedText, rules);

            if (chapters.Count == 0)
            {
                return new BookImportAnalysis(
                    BookImportAnalysisStatus.Failed,
                    filePath,
                    Path.GetFileName(filePath),
                    Path.GetFileNameWithoutExtension(filePath),
                    analyzedText.EncodingName,
                    analyzedText.PreviewText,
                    normalizedText,
                    sourceHash,
                    [],
                    BookImportFailureReason.NoValidChapters,
                    null);
            }

            return new BookImportAnalysis(
                BookImportAnalysisStatus.ReadyToCommit,
                filePath,
                Path.GetFileName(filePath),
                Path.GetFileNameWithoutExtension(filePath),
                analyzedText.EncodingName,
                analyzedText.PreviewText,
                normalizedText,
                sourceHash,
                chapters,
                null,
                null);
        }
        catch (DecoderFallbackException)
        {
            return new BookImportAnalysis(
                BookImportAnalysisStatus.Failed,
                filePath,
                Path.GetFileName(filePath),
                Path.GetFileNameWithoutExtension(filePath),
                "unknown",
                string.Empty,
                string.Empty,
                string.Empty,
                [],
                BookImportFailureReason.UnsupportedEncoding,
                null);
        }
        catch (IOException)
        {
            return new BookImportAnalysis(
                BookImportAnalysisStatus.Failed,
                filePath,
                Path.GetFileName(filePath),
                Path.GetFileNameWithoutExtension(filePath),
                "unknown",
                string.Empty,
                string.Empty,
                string.Empty,
                [],
                BookImportFailureReason.FileReadFailed,
                null);
        }
    }

    public async Task<BookImportResult> CommitAsync(BookImportAnalysis analysis, CancellationToken cancellationToken)
    {
        if (analysis.Status != BookImportAnalysisStatus.ReadyToCommit)
        {
            throw new InvalidOperationException("Only ReadyToCommit analysis results can be committed.");
        }

        var bookId = Guid.NewGuid().ToString();
        var copyHandle = await _bookFileStore.PrepareCopyAsync(analysis.OriginalFilePath, bookId, cancellationToken);
        var now = DateTime.UtcNow.ToString("O");

        var book = new Book(
            bookId,
            analysis.SuggestedTitle,
            null,
            analysis.OriginalFileName,
            copyHandle.FinalPath,
            analysis.SourceHash,
            analysis.DetectedEncoding,
            now,
            now);

        var chapters = analysis.Chapters
            .Select(chapter => new Chapter(
                Guid.NewGuid().ToString(),
                bookId,
                chapter.ChapterIndex,
                chapter.Title,
                chapter.Content,
                chapter.StartOffset,
                chapter.Length))
            .ToArray();

        try
        {
            await _bookImportRepository.SaveAsync(book, chapters, cancellationToken);
            await _bookFileStore.FinalizeAsync(copyHandle, cancellationToken);
        }
        catch
        {
            await _bookFileStore.CleanupAsync(copyHandle);
            throw;
        }

        return new BookImportResult(bookId, book.Title, chapters.Length);
    }
}
```

Update `src/NovelSpeaker.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs` to add:

```csharp
services.AddSingleton<IBookImportService, BookImportService>();
```

- [ ] **Step 4: Run the service tests to verify they pass**

Run:

```bash
dotnet test tests/NovelSpeaker.UnitTests/NovelSpeaker.UnitTests.csproj --filter "AnalyzeAsync_returns_duplicate_failure_when_hash_already_exists|CommitAsync_throws_when_analysis_is_not_ready"
```

Expected: PASS. Duplicate files should fail during analysis, and `CommitAsync` should reject non-ready analysis results.

- [ ] **Step 5: Commit**

```bash
git add src/NovelSpeaker.Application/Books/BookImportAnalysis.cs src/NovelSpeaker.Application/Books/BookImportAnalysisStatus.cs src/NovelSpeaker.Application/Books/BookImportFailureReason.cs src/NovelSpeaker.Application/Books/BookImportResult.cs src/NovelSpeaker.Application/Books/IBookImportService.cs src/NovelSpeaker.Infrastructure/Books/BookImportService.cs src/NovelSpeaker.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs tests/NovelSpeaker.UnitTests/Books/BookImportServiceTests.cs
git commit -m "feat(import): add two-phase book import service"
```

### Task 7: Reshape the shell and add the library import workflow

**Files:**
- Create: `src/NovelSpeaker.App/ViewModels/ChapterRulesViewModel.cs`
- Create: `src/NovelSpeaker.App/ViewModels/LibraryBookItemViewModel.cs`
- Create: `src/NovelSpeaker.App/ViewModels/LibraryViewModel.cs`
- Create: `src/NovelSpeaker.App/ViewModels/PlayerViewModel.cs`
- Create: `src/NovelSpeaker.App/ViewModels/SettingsViewModel.cs`
- Create: `src/NovelSpeaker.App/Views/ChapterRulesView.xaml`
- Create: `src/NovelSpeaker.App/Views/ChapterRulesView.xaml.cs`
- Create: `src/NovelSpeaker.App/Views/LibraryView.xaml`
- Create: `src/NovelSpeaker.App/Views/LibraryView.xaml.cs`
- Create: `src/NovelSpeaker.App/Views/PlayerView.xaml`
- Create: `src/NovelSpeaker.App/Views/PlayerView.xaml.cs`
- Create: `src/NovelSpeaker.App/Views/SettingsView.xaml`
- Create: `src/NovelSpeaker.App/Views/SettingsView.xaml.cs`
- Modify: `src/NovelSpeaker.App/App.xaml`
- Modify: `src/NovelSpeaker.App/MainWindow.xaml`
- Modify: `src/NovelSpeaker.App/MainWindow.xaml.cs`
- Modify: `src/NovelSpeaker.App/ServiceCollectionExtensions.cs`
- Modify: `src/NovelSpeaker.App/ViewModels/MainWindowViewModel.cs`
- Test: `tests/NovelSpeaker.UnitTests/ViewModels/LibraryViewModelTests.cs`

- [ ] **Step 1: Write the failing library workflow test**

Create `tests/NovelSpeaker.UnitTests/ViewModels/LibraryViewModelTests.cs`:

```csharp
using NovelSpeaker.Application.Books;
using NovelSpeaker.App.ViewModels;

namespace NovelSpeaker.UnitTests.ViewModels;

public sealed class LibraryViewModelTests
{
    [Fact]
    public async Task ImportSelectedFileAsync_refreshes_books_after_successful_commit()
    {
        var importService = new FakeBookImportService(
            new BookImportAnalysis(
                BookImportAnalysisStatus.ReadyToCommit,
                "C:\\books\\demo.txt",
                "demo.txt",
                "demo",
                "utf-8",
                "preview",
                "第一章 开始\n正文",
                "hash",
                [new BookImportChapter(0, "第一章 开始", "正文", 6, 2)],
                null,
                null),
            new BookImportResult("book-1", "demo", 1));

        var catalogService = new FakeBookCatalogService([
            new BookSummary("book-1", "demo", null, "第一章 开始", DateTime.UtcNow.ToString("O"))
        ]);

        var viewModel = new LibraryViewModel(importService, catalogService);

        await viewModel.ImportFileAsync("C:\\books\\demo.txt", CancellationToken.None);

        Assert.Single(viewModel.Books);
        Assert.Equal("demo", viewModel.Books[0].Title);
        Assert.Equal("导入成功：demo", viewModel.StatusMessage);
    }

    private sealed class FakeBookImportService : IBookImportService
    {
        private readonly BookImportAnalysis _analysis;
        private readonly BookImportResult _result;

        public FakeBookImportService(BookImportAnalysis analysis, BookImportResult result)
        {
            _analysis = analysis;
            _result = result;
        }

        public Task<BookImportAnalysis> AnalyzeAsync(string filePath, string? encodingName, CancellationToken cancellationToken)
        {
            return Task.FromResult(_analysis);
        }

        public Task<BookImportResult> CommitAsync(BookImportAnalysis analysis, CancellationToken cancellationToken)
        {
            return Task.FromResult(_result);
        }
    }

    private sealed class FakeBookCatalogService : IBookCatalogService
    {
        private readonly IReadOnlyList<BookSummary> _books;

        public FakeBookCatalogService(IReadOnlyList<BookSummary> books)
        {
            _books = books;
        }

        public Task<IReadOnlyList<BookSummary>> GetBooksAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(_books);
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run:

```bash
dotnet test tests/NovelSpeaker.UnitTests/NovelSpeaker.UnitTests.csproj --filter ImportSelectedFileAsync_refreshes_books_after_successful_commit
```

Expected: FAIL because the library view model and its workflow do not exist yet.

- [ ] **Step 3: Implement the shell layout and library page**

Create `src/NovelSpeaker.App/ViewModels/LibraryBookItemViewModel.cs`:

```csharp
namespace NovelSpeaker.App.ViewModels;

public sealed record LibraryBookItemViewModel(
    string Id,
    string Title,
    string? Author,
    string CurrentChapterTitle,
    string ImportedAt);
```

Create `src/NovelSpeaker.App/ViewModels/ChapterRulesViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Domain.Books;

namespace NovelSpeaker.App.ViewModels;

/// <summary>
/// Manages the global chapter-rule list used during import.
/// </summary>
public sealed partial class ChapterRulesViewModel : ObservableObject
{
    private readonly IChapterRuleRepository _repository;

    public ChapterRulesViewModel(IChapterRuleRepository repository)
    {
        _repository = repository;
    }

    public ObservableCollection<ChapterRule> Rules { get; } = [];

    [ObservableProperty]
    private string statusMessage = "在这里管理导入时使用的章节规则。";

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        var rules = await _repository.GetAllAsync(cancellationToken);
        Rules.Clear();
        foreach (var rule in rules)
        {
            Rules.Add(rule);
        }
    }

    public async Task ImportDefaultsAsync(CancellationToken cancellationToken)
    {
        await _repository.ImportDefaultsAsync(cancellationToken);
        await LoadAsync(cancellationToken);
        StatusMessage = "默认规则已导入。";
    }
}
```

Create `src/NovelSpeaker.App/ViewModels/LibraryViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovelSpeaker.Application.Books;

namespace NovelSpeaker.App.ViewModels;

/// <summary>
/// Drives the library page import experience and displays imported books.
/// </summary>
public sealed partial class LibraryViewModel : ObservableObject
{
    private readonly IBookImportService _bookImportService;
    private readonly IBookCatalogService _bookCatalogService;

    public LibraryViewModel(
        IBookImportService bookImportService,
        IBookCatalogService bookCatalogService)
    {
        _bookImportService = bookImportService;
        _bookCatalogService = bookCatalogService;
    }

    public ObservableCollection<LibraryBookItemViewModel> Books { get; } = [];

    [ObservableProperty]
    private string statusMessage = "导入一本 TXT，开始建立你的书库。";

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private bool isEncodingPreviewVisible;

    [ObservableProperty]
    private string previewText = string.Empty;

    [ObservableProperty]
    private string selectedEncoding = "utf-8";

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        var books = await _bookCatalogService.GetBooksAsync(cancellationToken);
        Books.Clear();

        foreach (var book in books)
        {
            Books.Add(new LibraryBookItemViewModel(
                book.Id,
                book.Title,
                book.Author,
                book.CurrentChapterTitle,
                book.ImportedAt));
        }
    }

    public async Task ImportFileAsync(string filePath, CancellationToken cancellationToken)
    {
        IsBusy = true;
        LastImportedPath = filePath;

        try
        {
            var analysis = await _bookImportService.AnalyzeAsync(filePath, null, cancellationToken);
            if (analysis.Status == BookImportAnalysisStatus.Failed)
            {
                PreviewText = analysis.PreviewText;
                IsEncodingPreviewVisible = analysis.FailureReason == BookImportFailureReason.UnsupportedEncoding;
                StatusMessage = analysis.FailureReason switch
                {
                    BookImportFailureReason.DuplicateBook => "这本书已经导入过了。",
                    BookImportFailureReason.NoValidChapters => "未识别到有效章节，请检查章节规则。",
                    BookImportFailureReason.UnsupportedEncoding => "自动识别编码失败，请切换编码后重试。",
                    _ => "导入失败，请重试。"
                };
                return;
            }

            var result = await _bookImportService.CommitAsync(analysis, cancellationToken);
            await LoadAsync(cancellationToken);
            StatusMessage = $"导入成功：{result.Title}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RetryWithEncodingAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(LastImportedPath))
        {
            return;
        }

        var analysis = await _bookImportService.AnalyzeAsync(LastImportedPath, SelectedEncoding, cancellationToken);
        if (analysis.Status == BookImportAnalysisStatus.ReadyToCommit)
        {
            var result = await _bookImportService.CommitAsync(analysis, cancellationToken);
            await LoadAsync(cancellationToken);
            StatusMessage = $"导入成功：{result.Title}";
            IsEncodingPreviewVisible = false;
        }
    }

    public string? LastImportedPath { get; private set; }
}
```

Create `src/NovelSpeaker.App/ViewModels/PlayerViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;

namespace NovelSpeaker.App.ViewModels;

public sealed partial class PlayerViewModel : ObservableObject
{
    [ObservableProperty]
    private string headline = "播放页将在后续纵向切片中接入真实播放流程。";
}
```

Create `src/NovelSpeaker.App/ViewModels/SettingsViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;

namespace NovelSpeaker.App.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    [ObservableProperty]
    private string headline = "设置页将在后续阶段接入缓存与播放偏好。";
}
```

Create `src/NovelSpeaker.App/Views/ChapterRulesView.xaml`:

```xml
<UserControl x:Class="NovelSpeaker.App.Views.ChapterRulesView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
        </Grid.RowDefinitions>

        <StackPanel Margin="0,0,0,16">
            <TextBlock FontSize="26" FontWeight="SemiBold" Text="章节规则" />
            <TextBlock Margin="0,8,0,0" Text="{Binding StatusMessage}" />
            <Button Width="160" Margin="0,12,0,0" Content="导入默认规则" Click="ImportDefaultsButton_OnClick" />
        </StackPanel>

        <DataGrid Grid.Row="1"
                  AutoGenerateColumns="False"
                  CanUserAddRows="False"
                  ItemsSource="{Binding Rules}">
            <DataGrid.Columns>
                <DataGridTextColumn Header="名称" Binding="{Binding Name}" Width="160" />
                <DataGridTextColumn Header="模式" Binding="{Binding Pattern}" Width="*" />
                <DataGridCheckBoxColumn Header="启用" Binding="{Binding IsEnabled}" Width="80" />
                <DataGridTextColumn Header="排序" Binding="{Binding SortOrder}" Width="80" />
            </DataGrid.Columns>
        </DataGrid>
    </Grid>
</UserControl>
```

Create `src/NovelSpeaker.App/Views/ChapterRulesView.xaml.cs`:

```csharp
using System.Windows.Controls;
using NovelSpeaker.App.ViewModels;

namespace NovelSpeaker.App.Views;

public partial class ChapterRulesView : UserControl
{
    public ChapterRulesView()
    {
        InitializeComponent();
    }

    private async void ImportDefaultsButton_OnClick(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is ChapterRulesViewModel viewModel)
        {
            await viewModel.ImportDefaultsAsync(CancellationToken.None);
        }
    }
}
```

Create `src/NovelSpeaker.App/Views/LibraryView.xaml`:

```xml
<UserControl x:Class="NovelSpeaker.App.Views.LibraryView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <UserControl.Resources>
        <BooleanToVisibilityConverter x:Key="BooleanToVisibilityConverter" />
    </UserControl.Resources>
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
        </Grid.RowDefinitions>

        <StackPanel>
            <TextBlock FontSize="28" FontWeight="SemiBold" Text="书库" />
            <TextBlock Margin="0,10,0,0" Text="{Binding StatusMessage}" />
        </StackPanel>

        <Border Grid.Row="1"
                Margin="0,20,0,20"
                Padding="24"
                Background="#FFF6ECD8"
                BorderBrush="#FFD9BA84"
                BorderThickness="1"
                CornerRadius="16"
                AllowDrop="True"
                DragEnter="ImportBorder_OnDragEnter"
                Drop="ImportBorder_OnDrop">
            <StackPanel>
                <Button Width="180" Content="导入小说" Click="ImportButton_OnClick" />
                <TextBlock Margin="0,12,0,0" Text="把 TXT 文件拖到这里，或使用上面的按钮选择文件。" />
                <Border Margin="0,16,0,0"
                        Padding="16"
                        Background="#FFFFFBF4"
                        CornerRadius="12"
                        Visibility="{Binding IsEncodingPreviewVisible, Converter={StaticResource BooleanToVisibilityConverter}}">
                    <StackPanel>
                        <TextBlock FontWeight="SemiBold" Text="编码预览" />
                        <TextBox Margin="0,12,0,12"
                                 Height="160"
                                 AcceptsReturn="True"
                                 IsReadOnly="True"
                                 TextWrapping="Wrap"
                                 Text="{Binding PreviewText}" />
                        <StackPanel Orientation="Horizontal">
                            <ComboBox Width="160"
                                      SelectedValuePath="Content"
                                      SelectedValue="{Binding SelectedEncoding}">
                                <ComboBoxItem Content="utf-8" />
                                <ComboBoxItem Content="gb18030" />
                            </ComboBox>
                            <Button Margin="12,0,0,0" Content="按此编码重试" Click="RetryEncodingButton_OnClick" />
                        </StackPanel>
                    </StackPanel>
                </Border>
            </StackPanel>
        </Border>

        <DataGrid Grid.Row="2"
                  AutoGenerateColumns="False"
                  CanUserAddRows="False"
                  ItemsSource="{Binding Books}">
            <DataGrid.Columns>
                <DataGridTextColumn Header="书名" Binding="{Binding Title}" Width="2*" />
                <DataGridTextColumn Header="作者" Binding="{Binding Author}" Width="*" />
                <DataGridTextColumn Header="当前章节" Binding="{Binding CurrentChapterTitle}" Width="2*" />
                <DataGridTextColumn Header="导入时间" Binding="{Binding ImportedAt}" Width="*" />
            </DataGrid.Columns>
        </DataGrid>
    </Grid>
</UserControl>
```

Create `src/NovelSpeaker.App/Views/LibraryView.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using NovelSpeaker.App.ViewModels;
using Microsoft.Win32;

namespace NovelSpeaker.App.Views;

public partial class LibraryView : UserControl
{
    public LibraryView()
    {
        InitializeComponent();
    }

    private async void ImportButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not LibraryViewModel viewModel)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
        {
            await viewModel.ImportFileAsync(dialog.FileName, CancellationToken.None);
        }
    }

    private void ImportBorder_OnDragEnter(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void ImportBorder_OnDrop(object sender, DragEventArgs e)
    {
        if (DataContext is not LibraryViewModel viewModel)
        {
            return;
        }

        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
        {
            await viewModel.ImportFileAsync(files[0], CancellationToken.None);
        }
    }

    private async void RetryEncodingButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is LibraryViewModel viewModel)
        {
            await viewModel.RetryWithEncodingCommand.ExecuteAsync(null);
        }
    }
}
```

Create `src/NovelSpeaker.App/Views/PlayerView.xaml`:

```xml
<UserControl x:Class="NovelSpeaker.App.Views.PlayerView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Grid>
        <TextBlock FontSize="28"
                   FontWeight="SemiBold"
                   TextWrapping="Wrap"
                   Text="{Binding Headline}" />
    </Grid>
</UserControl>
```

Create `src/NovelSpeaker.App/Views/PlayerView.xaml.cs`:

```csharp
using System.Windows.Controls;

namespace NovelSpeaker.App.Views;

public partial class PlayerView : UserControl
{
    public PlayerView()
    {
        InitializeComponent();
    }
}
```

Create `src/NovelSpeaker.App/Views/SettingsView.xaml`:

```xml
<UserControl x:Class="NovelSpeaker.App.Views.SettingsView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Grid>
        <TextBlock FontSize="28"
                   FontWeight="SemiBold"
                   TextWrapping="Wrap"
                   Text="{Binding Headline}" />
    </Grid>
</UserControl>
```

Create `src/NovelSpeaker.App/Views/SettingsView.xaml.cs`:

```csharp
using System.Windows.Controls;

namespace NovelSpeaker.App.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }
}
```

Update `src/NovelSpeaker.App/ViewModels/MainWindowViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;

namespace NovelSpeaker.App.ViewModels;

/// <summary>
/// Hosts the top-level pages for the desktop shell.
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    public MainWindowViewModel(
        LibraryViewModel libraryViewModel,
        PlayerViewModel playerViewModel,
        ChapterRulesViewModel chapterRulesViewModel,
        SettingsViewModel settingsViewModel)
    {
        Library = libraryViewModel;
        Player = playerViewModel;
        Rules = chapterRulesViewModel;
        Settings = settingsViewModel;
        CurrentPage = Library;
    }

    public LibraryViewModel Library { get; }
    public PlayerViewModel Player { get; }
    public ChapterRulesViewModel Rules { get; }
    public SettingsViewModel Settings { get; }

    [ObservableProperty]
    private object currentPage;

    public void ShowLibrary() => CurrentPage = Library;
    public void ShowPlayer() => CurrentPage = Player;
    public void ShowRules() => CurrentPage = Rules;
    public void ShowSettings() => CurrentPage = Settings;
}
```

Update `src/NovelSpeaker.App/ServiceCollectionExtensions.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using NovelSpeaker.App.ViewModels;

namespace NovelSpeaker.App;

/// <summary>
/// Registers desktop-specific views and view models.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddNovelSpeakerDesktop(this IServiceCollection services)
    {
        services.AddSingleton<LibraryViewModel>();
        services.AddSingleton<PlayerViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<ChapterRulesViewModel>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>();
        return services;
    }
}
```

Update `src/NovelSpeaker.App/App.xaml` to add data templates:

```xml
<Application x:Class="NovelSpeaker.App.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:viewModels="clr-namespace:NovelSpeaker.App.ViewModels"
             xmlns:views="clr-namespace:NovelSpeaker.App.Views">
    <Application.Resources>
        <DataTemplate DataType="{x:Type viewModels:LibraryViewModel}">
            <views:LibraryView />
        </DataTemplate>
        <DataTemplate DataType="{x:Type viewModels:PlayerViewModel}">
            <views:PlayerView />
        </DataTemplate>
        <DataTemplate DataType="{x:Type viewModels:SettingsViewModel}">
            <views:SettingsView />
        </DataTemplate>
        <DataTemplate DataType="{x:Type viewModels:ChapterRulesViewModel}">
            <views:ChapterRulesView />
        </DataTemplate>
    </Application.Resources>
</Application>
```

Update `src/NovelSpeaker.App/MainWindow.xaml`:

```xml
<Window x:Class="NovelSpeaker.App.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="NovelSpeaker"
        Height="760"
        Width="1180">
    <Grid Background="#FFF5F2EA">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
            <RowDefinition Height="Auto" />
        </Grid.RowDefinitions>

        <Border Grid.Row="0" Padding="24" Background="#FFF0E5D1">
            <StackPanel Orientation="Horizontal">
                <Button Margin="0,0,12,0" Content="书库" Click="LibraryButton_OnClick" />
                <Button Margin="0,0,12,0" Content="播放" Click="PlayerButton_OnClick" />
                <Button Margin="0,0,12,0" Content="规则" Click="RulesButton_OnClick" />
                <Button Content="设置" Click="SettingsButton_OnClick" />
            </StackPanel>
        </Border>

        <Border Grid.Row="1" Margin="24" Padding="24" Background="White" CornerRadius="18">
            <ContentControl Content="{Binding CurrentPage}" />
        </Border>

        <Border Grid.Row="2" Padding="20" Background="#FF1D3557">
            <Grid>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*" />
                    <ColumnDefinition Width="Auto" />
                </Grid.ColumnDefinitions>
                <StackPanel>
                    <TextBlock Foreground="White" FontSize="16" FontWeight="SemiBold" Text="未开始播放" />
                    <TextBlock Foreground="#FFD8E2EC" Text="导入后即可在这里看到当前书籍、章节与播放控制。" />
                </StackPanel>
                <Button Grid.Column="1" MinWidth="120" Padding="20,10" Content="播放 / 暂停" />
            </Grid>
        </Border>
    </Grid>
</Window>
```

Update `src/NovelSpeaker.App/MainWindow.xaml.cs`:

```csharp
using System.Windows;
using NovelSpeaker.App.ViewModels;

namespace NovelSpeaker.App;

/// <summary>
/// Hosts the application shell and forwards top-level navigation clicks.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;

    public MainWindow(MainWindowViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
    }

    private void LibraryButton_OnClick(object sender, RoutedEventArgs e) => _viewModel.ShowLibrary();
    private void PlayerButton_OnClick(object sender, RoutedEventArgs e) => _viewModel.ShowPlayer();
    private void RulesButton_OnClick(object sender, RoutedEventArgs e) => _viewModel.ShowRules();
    private void SettingsButton_OnClick(object sender, RoutedEventArgs e) => _viewModel.ShowSettings();
}
```

- [ ] **Step 4: Run the library workflow test to verify it passes**

Run:

```bash
dotnet test tests/NovelSpeaker.UnitTests/NovelSpeaker.UnitTests.csproj --filter ImportSelectedFileAsync_refreshes_books_after_successful_commit
```

Expected: PASS. A successful import should refresh the in-memory library list and set a success status message.

- [ ] **Step 5: Commit**

```bash
git add src/NovelSpeaker.App/App.xaml src/NovelSpeaker.App/MainWindow.xaml src/NovelSpeaker.App/MainWindow.xaml.cs src/NovelSpeaker.App/ServiceCollectionExtensions.cs src/NovelSpeaker.App/ViewModels/MainWindowViewModel.cs src/NovelSpeaker.App/ViewModels/ChapterRulesViewModel.cs src/NovelSpeaker.App/ViewModels/LibraryBookItemViewModel.cs src/NovelSpeaker.App/ViewModels/LibraryViewModel.cs src/NovelSpeaker.App/ViewModels/PlayerViewModel.cs src/NovelSpeaker.App/ViewModels/SettingsViewModel.cs src/NovelSpeaker.App/Views/ChapterRulesView.xaml src/NovelSpeaker.App/Views/ChapterRulesView.xaml.cs src/NovelSpeaker.App/Views/LibraryView.xaml src/NovelSpeaker.App/Views/LibraryView.xaml.cs src/NovelSpeaker.App/Views/PlayerView.xaml src/NovelSpeaker.App/Views/PlayerView.xaml.cs src/NovelSpeaker.App/Views/SettingsView.xaml src/NovelSpeaker.App/Views/SettingsView.xaml.cs tests/NovelSpeaker.UnitTests/ViewModels/LibraryViewModelTests.cs
git commit -m "feat(app): add library shell and import workflow"
```

### Task 8: Deepen the chapter-rules management page

**Files:**
- Create: `src/NovelSpeaker.App/ViewModels/ChapterRulesViewModel.cs`
- Create: `src/NovelSpeaker.App/Views/ChapterRulesView.xaml`
- Create: `src/NovelSpeaker.App/Views/ChapterRulesView.xaml.cs`
- Modify: `src/NovelSpeaker.App/ServiceCollectionExtensions.cs`
- Modify: `tests/NovelSpeaker.UnitTests/DependencyInjection/ServiceCollectionExtensionsTests.cs`
- Test: `tests/NovelSpeaker.UnitTests/ViewModels/ChapterRulesViewModelTests.cs`

- [ ] **Step 1: Write the failing rules-page test**

Create `tests/NovelSpeaker.UnitTests/ViewModels/ChapterRulesViewModelTests.cs`:

```csharp
using NovelSpeaker.App.ViewModels;
using NovelSpeaker.Domain.Books;

namespace NovelSpeaker.UnitTests.ViewModels;

public sealed class ChapterRulesViewModelTests
{
    [Fact]
    public async Task ImportDefaultsAsync_refreshes_rule_rows_and_sets_status()
    {
        var repository = new FakeChapterRuleRepository([
            new ChapterRule("1", "章节数字", @"^\s*第[0-9]+章.*$", 10, true, "now", "now")
        ]);

        var viewModel = new ChapterRulesViewModel(repository);
        await viewModel.LoadAsync(CancellationToken.None);
        await viewModel.ImportDefaultsAsync(CancellationToken.None);

        Assert.True(viewModel.Rules.Count >= 1);
        Assert.Equal("默认规则已导入。", viewModel.StatusMessage);
    }

    private sealed class FakeChapterRuleRepository : IChapterRuleRepository
    {
        private readonly List<ChapterRule> _rules;

        public FakeChapterRuleRepository(IReadOnlyList<ChapterRule> rules)
        {
            _rules = rules.ToList();
        }

        public Task<IReadOnlyList<ChapterRule>> GetAllAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<ChapterRule>>(_rules.ToArray());
        }

        public Task<IReadOnlyList<ChapterRule>> GetEnabledAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<ChapterRule>>(_rules.Where(rule => rule.IsEnabled).ToArray());
        }

        public Task SaveAsync(ChapterRule rule, CancellationToken cancellationToken)
        {
            _rules.RemoveAll(item => item.Id == rule.Id);
            _rules.Add(rule);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string ruleId, CancellationToken cancellationToken)
        {
            _rules.RemoveAll(item => item.Id == ruleId);
            return Task.CompletedTask;
        }

        public Task MoveAsync(string ruleId, int newSortOrder, CancellationToken cancellationToken)
        {
            var index = _rules.FindIndex(item => item.Id == ruleId);
            if (index >= 0)
            {
                _rules[index] = _rules[index] with { SortOrder = newSortOrder };
            }

            return Task.CompletedTask;
        }

        public Task<int> ImportDefaultsAsync(CancellationToken cancellationToken)
        {
            if (_rules.All(rule => rule.Name != "默认规则"))
            {
                _rules.Add(new ChapterRule("default-1", "默认规则", @"^\s*第.+$", 99, true, "now", "now"));
                return Task.FromResult(1);
            }

            return Task.FromResult(0);
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run:

```bash
dotnet test tests/NovelSpeaker.UnitTests/NovelSpeaker.UnitTests.csproj --filter ImportDefaultsAsync_refreshes_rule_rows_and_sets_status
```

Expected: FAIL because the rules page view model does not exist yet.

- [ ] **Step 3: Implement the rules page and wire it into DI**

Update `src/NovelSpeaker.App/ViewModels/ChapterRulesViewModel.cs` to add save and delete flows:

```csharp
public async Task SaveRuleAsync(ChapterRule rule, CancellationToken cancellationToken)
{
    await _repository.SaveAsync(rule, cancellationToken);
    await LoadAsync(cancellationToken);
    StatusMessage = $"已保存规则：{rule.Name}";
}

public async Task DeleteRuleAsync(string ruleId, CancellationToken cancellationToken)
{
    await _repository.DeleteAsync(ruleId, cancellationToken);
    await LoadAsync(cancellationToken);
    StatusMessage = "规则已删除。";
}
```

Update `src/NovelSpeaker.App/Views/ChapterRulesView.xaml` to expose the management layout:

```xml
<UserControl x:Class="NovelSpeaker.App.Views.ChapterRulesView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
        </Grid.RowDefinitions>

        <StackPanel Margin="0,0,0,16">
            <TextBlock FontSize="26" FontWeight="SemiBold" Text="章节规则" />
            <TextBlock Margin="0,8,0,0" Text="{Binding StatusMessage}" />
            <Button Width="160" Margin="0,12,0,0" Content="导入默认规则" Click="ImportDefaultsButton_OnClick" />
            <TextBlock Margin="0,12,0,0" Text="首版至少支持新增、修改、删除、启用/禁用与排序调整。" />
        </StackPanel>

        <DataGrid Grid.Row="1"
                  AutoGenerateColumns="False"
                  CanUserAddRows="False"
                  ItemsSource="{Binding Rules}">
            <DataGrid.Columns>
                <DataGridTextColumn Header="名称" Binding="{Binding Name}" Width="160" />
                <DataGridTextColumn Header="模式" Binding="{Binding Pattern}" Width="*" />
                <DataGridCheckBoxColumn Header="启用" Binding="{Binding IsEnabled}" Width="80" />
                <DataGridTextColumn Header="排序" Binding="{Binding SortOrder}" Width="80" />
            </DataGrid.Columns>
        </DataGrid>
    </Grid>
</UserControl>
```

Update `tests/NovelSpeaker.UnitTests/DependencyInjection/ServiceCollectionExtensionsTests.cs` so it also resolves the page view model:

```csharp
Assert.IsType<ChapterRulesViewModel>(provider.GetRequiredService<ChapterRulesViewModel>());
```

- [ ] **Step 4: Run the rules-page test to verify it passes**

Run:

```bash
dotnet test tests/NovelSpeaker.UnitTests/NovelSpeaker.UnitTests.csproj --filter ImportDefaultsAsync_refreshes_rule_rows_and_sets_status
```

Expected: PASS. Importing defaults from the page should reload the rule rows and update the status message.

- [ ] **Step 5: Commit**

```bash
git add src/NovelSpeaker.App/ViewModels/ChapterRulesViewModel.cs src/NovelSpeaker.App/Views/ChapterRulesView.xaml tests/NovelSpeaker.UnitTests/ViewModels/ChapterRulesViewModelTests.cs tests/NovelSpeaker.UnitTests/DependencyInjection/ServiceCollectionExtensionsTests.cs
git commit -m "feat(app): add chapter rules management page"
```

### Task 9: Run full verification and manual acceptance

**Files:**
- Modify: none
- Test: `tests/NovelSpeaker.UnitTests/NovelSpeaker.UnitTests.csproj`

- [ ] **Step 1: Run formatting verification**

Run:

```bash
dotnet format --verify-no-changes
```

Expected: PASS. If it fails, run `dotnet format`, inspect the diff, and commit only the formatting required by the new import slice.

- [ ] **Step 2: Run the release build**

Run:

```bash
dotnet build -c Release
```

Expected: PASS. All projects should compile, including WPF views, the infrastructure import pipeline, and the tests.

- [ ] **Step 3: Run the release tests**

Run:

```bash
dotnet test -c Release
```

Expected: PASS. At minimum, chapter splitting, hashing, normalization, duplicate detection, transactional persistence, and the library / rules view models should all be covered.

- [ ] **Step 4: Perform manual verification**

Perform this manual script:

```text
1. Start the app with an empty LocalAppData directory.
2. Confirm the shell opens on the 书库 page with top navigation and a visible bottom player bar.
3. Import a UTF-8 TXT file that contains "第一章" style headings and verify the book appears in the list.
4. Re-import the exact same file and verify the UI reports that the book already exists.
5. Import a TXT file encoded as GB18030 and verify fallback decoding succeeds.
6. Import a TXT file with no valid chapters and verify the UI shows the rules-related failure message.
7. Open the 规则 page, import default rules again, and confirm no duplicate rows appear for exact `(Name, Pattern)` matches.
8. Drag and drop a TXT file onto the library import area and confirm it follows the same analysis and commit path.
```

Expected: the feature behaves according to the design spec without leaving duplicate DB rows or orphaned `.tmp` files.

- [ ] **Step 5: Commit**

```bash
git add .
git commit -m "test(import): verify txt import and chapter splitting slice"
```

## Self-Review

### Spec coverage

- TXT file picker and drag-drop entry: covered by Task 7.
- BOM / UTF-8 / GB18030 analysis and preview: covered by Task 3 and Task 6.
- SHA-256 duplicate detection: covered by Task 5 and Task 6.
- No half-import on invalid chapters: covered by Task 4 and Task 6.
- `Books` + `Chapters` transaction and file copy: covered by Task 5 and Task 6.
- Global rule library, enable / disable / order / import defaults: persistence in Task 2, UI entry in Task 8.
- UI consistency with `docs/06_UI_AND_USER_FLOWS.md`: shell structure added in Task 7, rules page in Task 8.

### Placeholder scan

- No `TODO`, `TBD`, “similar to previous task”, or “handle appropriately” placeholders remain.
- Every task includes exact file paths, commands, and concrete code snippets.

### Type consistency

- `BookImportAnalysis`, `BookImportChapter`, `BookImportResult`, `IChapterRuleRepository`, `IBookImportService`, and `BookSummary` are named consistently across later tasks.
- `ChapterRulesViewModel` now lands in Task 7 before shell wiring, and Task 8 only deepens the page behavior.
