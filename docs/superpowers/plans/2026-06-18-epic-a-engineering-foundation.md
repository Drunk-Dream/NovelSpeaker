# Epic A Engineering Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the Epic A engineering foundation for NovelSpeaker with layered projects, minimal MVVM wiring, app data directory services, SQLite migration infrastructure, unit tests, and Windows CI.

**Architecture:** Keep the application intentionally small and vertical. `NovelSpeaker.App` becomes the WPF composition root, `NovelSpeaker.Application` defines startup-facing contracts, `NovelSpeaker.Infrastructure` implements file system and SQLite startup services, and `NovelSpeaker.UnitTests` locks the foundation down with TDD around directory rules, migration idempotency, and service registration.

**Tech Stack:** C#, .NET 10, WPF, CommunityToolkit.Mvvm, Microsoft.Data.Sqlite, Microsoft.Extensions.DependencyInjection, Microsoft.Extensions.Logging, xUnit, GitHub Actions

---

## File Structure

### New files

- `Directory.Build.props`
- `Directory.Packages.props`
- `src/NovelSpeaker.Domain/NovelSpeaker.Domain.csproj`
- `src/NovelSpeaker.Domain/Common/AppInfo.cs`
- `src/NovelSpeaker.Application/NovelSpeaker.Application.csproj`
- `src/NovelSpeaker.Application/Abstractions/IAppDataDirectoryProvider.cs`
- `src/NovelSpeaker.Application/Abstractions/IDatabaseInitializer.cs`
- `src/NovelSpeaker.Application/Abstractions/ISqliteConnectionFactory.cs`
- `src/NovelSpeaker.Infrastructure/NovelSpeaker.Infrastructure.csproj`
- `src/NovelSpeaker.Infrastructure/FileSystem/LocalAppDataDirectoryProvider.cs`
- `src/NovelSpeaker.Infrastructure/Persistence/SqliteConnectionFactory.cs`
- `src/NovelSpeaker.Infrastructure/Persistence/SqliteMigration.cs`
- `src/NovelSpeaker.Infrastructure/Persistence/SqliteMigrationRunner.cs`
- `src/NovelSpeaker.Infrastructure/Persistence/StartupDatabaseInitializer.cs`
- `src/NovelSpeaker.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs`
- `src/NovelSpeaker.App/ViewModels/MainWindowViewModel.cs`
- `tests/NovelSpeaker.UnitTests/NovelSpeaker.UnitTests.csproj`
- `tests/NovelSpeaker.UnitTests/Common/AppInfoTests.cs`
- `tests/NovelSpeaker.UnitTests/FileSystem/LocalAppDataDirectoryProviderTests.cs`
- `tests/NovelSpeaker.UnitTests/Persistence/SqliteMigrationRunnerTests.cs`
- `tests/NovelSpeaker.UnitTests/DependencyInjection/ServiceCollectionExtensionsTests.cs`
- `.github/workflows/ci.yml`

### Modified files

- `NovelSpeaker.slnx`
- `src/NovelSpeaker.App/NovelSpeaker.App.csproj`
- `src/NovelSpeaker.App/App.xaml`
- `src/NovelSpeaker.App/App.xaml.cs`
- `src/NovelSpeaker.App/MainWindow.xaml`
- `src/NovelSpeaker.App/MainWindow.xaml.cs`
- `docs/11_TASK_BACKLOG.md`

---

### Task 1: Create the solution skeleton and shared build configuration

**Files:**
- Create: `Directory.Build.props`
- Create: `Directory.Packages.props`
- Create: `src/NovelSpeaker.Domain/NovelSpeaker.Domain.csproj`
- Create: `src/NovelSpeaker.Domain/Common/AppInfo.cs`
- Create: `src/NovelSpeaker.Application/NovelSpeaker.Application.csproj`
- Create: `src/NovelSpeaker.Infrastructure/NovelSpeaker.Infrastructure.csproj`
- Create: `tests/NovelSpeaker.UnitTests/NovelSpeaker.UnitTests.csproj`
- Create: `tests/NovelSpeaker.UnitTests/Common/AppInfoTests.cs`
- Modify: `NovelSpeaker.slnx`
- Modify: `src/NovelSpeaker.App/NovelSpeaker.App.csproj`

- [ ] **Step 1: Write the failing solution-structure check**

Add this test file first at `tests/NovelSpeaker.UnitTests/Common/AppInfoTests.cs`:

```csharp
using NovelSpeaker.Domain.Common;

namespace NovelSpeaker.UnitTests.Common;

public sealed class AppInfoTests
{
    [Fact]
    public void ProductName_is_stable()
    {
        Assert.Equal("NovelSpeaker", AppInfo.ProductName);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run:

```bash
dotnet test tests/NovelSpeaker.UnitTests/NovelSpeaker.UnitTests.csproj --filter ProductName_is_stable
```

Expected: fail because the test project and referenced domain project do not exist yet.

- [ ] **Step 3: Create the shared build files and project skeleton**

Create `Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <AnalysisLevel>latest</AnalysisLevel>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <LangVersion>preview</LangVersion>
  </PropertyGroup>
</Project>
```

Create `Directory.Packages.props`:

```xml
<Project>
  <ItemGroup>
    <PackageVersion Include="CommunityToolkit.Mvvm" Version="8.4.0" />
    <PackageVersion Include="Microsoft.Data.Sqlite" Version="9.0.6" />
    <PackageVersion Include="Microsoft.Extensions.DependencyInjection" Version="9.0.6" />
    <PackageVersion Include="Microsoft.Extensions.Logging" Version="9.0.6" />
    <PackageVersion Include="Microsoft.Extensions.Logging.Debug" Version="9.0.6" />
    <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageVersion Include="xunit" Version="2.9.3" />
    <PackageVersion Include="xunit.runner.visualstudio" Version="3.1.3" />
    <PackageVersion Include="coverlet.collector" Version="6.0.4" />
  </ItemGroup>
</Project>
```

Create `src/NovelSpeaker.Domain/NovelSpeaker.Domain.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
</Project>
```

Create `src/NovelSpeaker.Domain/Common/AppInfo.cs`:

```csharp
namespace NovelSpeaker.Domain.Common;

/// <summary>
/// Provides application-wide constants shared by startup components.
/// </summary>
public static class AppInfo
{
    public const string ProductName = "NovelSpeaker";
}
```

Create `src/NovelSpeaker.Application/NovelSpeaker.Application.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="..\\NovelSpeaker.Domain\\NovelSpeaker.Domain.csproj" />
  </ItemGroup>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
</Project>
```

Create `src/NovelSpeaker.Infrastructure/NovelSpeaker.Infrastructure.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="..\\NovelSpeaker.Application\\NovelSpeaker.Application.csproj" />
    <ProjectReference Include="..\\NovelSpeaker.Domain\\NovelSpeaker.Domain.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Data.Sqlite" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="9.0.6" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="9.0.6" />
  </ItemGroup>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
</Project>
```

Create `tests/NovelSpeaker.UnitTests/NovelSpeaker.UnitTests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="..\\..\\src\\NovelSpeaker.Application\\NovelSpeaker.Application.csproj" />
    <ProjectReference Include="..\\..\\src\\NovelSpeaker.Infrastructure\\NovelSpeaker.Infrastructure.csproj" />
    <ProjectReference Include="..\\..\\src\\NovelSpeaker.App\\NovelSpeaker.App.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="coverlet.collector" />
  </ItemGroup>
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
</Project>
```

Update `src/NovelSpeaker.App/NovelSpeaker.App.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="..\\NovelSpeaker.Application\\NovelSpeaker.Application.csproj" />
    <ProjectReference Include="..\\NovelSpeaker.Infrastructure\\NovelSpeaker.Infrastructure.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="CommunityToolkit.Mvvm" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" />
    <PackageReference Include="Microsoft.Extensions.Logging" />
    <PackageReference Include="Microsoft.Extensions.Logging.Debug" />
  </ItemGroup>
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
  </PropertyGroup>
</Project>
```

Update `NovelSpeaker.slnx`:

```xml
<Solution>
  <Folder Name="/src/">
    <Project Path="src/NovelSpeaker.App/NovelSpeaker.App.csproj" />
    <Project Path="src/NovelSpeaker.Application/NovelSpeaker.Application.csproj" />
    <Project Path="src/NovelSpeaker.Domain/NovelSpeaker.Domain.csproj" />
    <Project Path="src/NovelSpeaker.Infrastructure/NovelSpeaker.Infrastructure.csproj" />
  </Folder>
  <Folder Name="/tests/">
    <Project Path="tests/NovelSpeaker.UnitTests/NovelSpeaker.UnitTests.csproj" />
  </Folder>
</Solution>
```

- [ ] **Step 4: Run restore and test to verify the project skeleton is wired**

Run:

```bash
dotnet restore
dotnet test tests/NovelSpeaker.UnitTests/NovelSpeaker.UnitTests.csproj --filter ProductName_is_stable
```

Expected: compilation succeeds, and the test passes because the shared project skeleton now exists.

- [ ] **Step 5: Commit the scaffold**

```bash
git add Directory.Build.props Directory.Packages.props NovelSpeaker.slnx src/NovelSpeaker.App/NovelSpeaker.App.csproj src/NovelSpeaker.Domain src/NovelSpeaker.Application src/NovelSpeaker.Infrastructure tests/NovelSpeaker.UnitTests
git commit -m "build: add layered solution skeleton"
```

---

### Task 2: Add the application abstractions and implement the app data directory provider

**Files:**
- Create: `src/NovelSpeaker.Application/Abstractions/IAppDataDirectoryProvider.cs`
- Create: `src/NovelSpeaker.Application/Abstractions/IDatabaseInitializer.cs`
- Create: `src/NovelSpeaker.Application/Abstractions/ISqliteConnectionFactory.cs`
- Create: `src/NovelSpeaker.Infrastructure/FileSystem/LocalAppDataDirectoryProvider.cs`
- Create: `tests/NovelSpeaker.UnitTests/FileSystem/LocalAppDataDirectoryProviderTests.cs`

- [ ] **Step 1: Write the failing directory provider tests**

Create `tests/NovelSpeaker.UnitTests/FileSystem/LocalAppDataDirectoryProviderTests.cs`:

```csharp
using NovelSpeaker.Infrastructure.FileSystem;

namespace NovelSpeaker.UnitTests.FileSystem;

public sealed class LocalAppDataDirectoryProviderTests
{
    [Fact]
    public async Task EnsureCreatedAsync_creates_expected_directories()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var provider = new LocalAppDataDirectoryProvider(root);

        await provider.EnsureCreatedAsync(CancellationToken.None);

        Assert.True(Directory.Exists(provider.RootDirectoryPath));
        Assert.True(Directory.Exists(provider.BooksDirectoryPath));
        Assert.True(Directory.Exists(provider.CacheDirectoryPath));
        Assert.True(Directory.Exists(provider.LogsDirectoryPath));
    }

    [Fact]
    public void Constructor_exposes_expected_database_path()
    {
        var root = Path.Combine("C:\\Temp", "NovelSpeaker");
        var provider = new LocalAppDataDirectoryProvider(root);

        Assert.Equal(Path.Combine(root, "app.db"), provider.DatabasePath);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run:

```bash
dotnet test tests/NovelSpeaker.UnitTests/NovelSpeaker.UnitTests.csproj --filter LocalAppDataDirectoryProviderTests
```

Expected: fail because the provider and abstractions do not exist yet.

- [ ] **Step 3: Add the abstractions and the minimal provider**

Create `src/NovelSpeaker.Application/Abstractions/IAppDataDirectoryProvider.cs`:

```csharp
namespace NovelSpeaker.Application.Abstractions;

/// <summary>
/// Resolves and creates application-owned storage directories.
/// </summary>
public interface IAppDataDirectoryProvider
{
    string RootDirectoryPath { get; }
    string DatabasePath { get; }
    string BooksDirectoryPath { get; }
    string CacheDirectoryPath { get; }
    string LogsDirectoryPath { get; }

    Task EnsureCreatedAsync(CancellationToken cancellationToken);
}
```

Create `src/NovelSpeaker.Application/Abstractions/IDatabaseInitializer.cs`:

```csharp
namespace NovelSpeaker.Application.Abstractions;

/// <summary>
/// Prepares the local database for application startup.
/// </summary>
public interface IDatabaseInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken);
}
```

Create `src/NovelSpeaker.Application/Abstractions/ISqliteConnectionFactory.cs`:

```csharp
using Microsoft.Data.Sqlite;

namespace NovelSpeaker.Application.Abstractions;

/// <summary>
/// Opens SQLite connections for startup and repository operations.
/// </summary>
public interface ISqliteConnectionFactory
{
    Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken);
}
```

Create `src/NovelSpeaker.Infrastructure/FileSystem/LocalAppDataDirectoryProvider.cs`:

```csharp
using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Domain.Common;

namespace NovelSpeaker.Infrastructure.FileSystem;

/// <summary>
/// Creates and exposes the app-owned directory structure under LocalAppData.
/// </summary>
public sealed class LocalAppDataDirectoryProvider : IAppDataDirectoryProvider
{
    public LocalAppDataDirectoryProvider()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppInfo.ProductName))
    {
    }

    public LocalAppDataDirectoryProvider(string rootDirectoryPath)
    {
        RootDirectoryPath = rootDirectoryPath;
        DatabasePath = Path.Combine(rootDirectoryPath, "app.db");
        BooksDirectoryPath = Path.Combine(rootDirectoryPath, "Books");
        CacheDirectoryPath = Path.Combine(rootDirectoryPath, "Cache");
        LogsDirectoryPath = Path.Combine(rootDirectoryPath, "Logs");
    }

    public string RootDirectoryPath { get; }
    public string DatabasePath { get; }
    public string BooksDirectoryPath { get; }
    public string CacheDirectoryPath { get; }
    public string LogsDirectoryPath { get; }

    public Task EnsureCreatedAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Directory.CreateDirectory(RootDirectoryPath);
        Directory.CreateDirectory(BooksDirectoryPath);
        Directory.CreateDirectory(CacheDirectoryPath);
        Directory.CreateDirectory(LogsDirectoryPath);

        return Task.CompletedTask;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run:

```bash
dotnet test tests/NovelSpeaker.UnitTests/NovelSpeaker.UnitTests.csproj --filter LocalAppDataDirectoryProviderTests
```

Expected: PASS.

- [ ] **Step 5: Commit the directory provider**

```bash
git add src/NovelSpeaker.Application/Abstractions src/NovelSpeaker.Infrastructure/FileSystem tests/NovelSpeaker.UnitTests/FileSystem
git commit -m "feat: add app data directory provider"
```

---

### Task 3: Add SQLite connection and migration infrastructure with TDD

**Files:**
- Create: `src/NovelSpeaker.Infrastructure/Persistence/SqliteConnectionFactory.cs`
- Create: `src/NovelSpeaker.Infrastructure/Persistence/SqliteMigration.cs`
- Create: `src/NovelSpeaker.Infrastructure/Persistence/SqliteMigrationRunner.cs`
- Create: `src/NovelSpeaker.Infrastructure/Persistence/StartupDatabaseInitializer.cs`
- Create: `tests/NovelSpeaker.UnitTests/Persistence/SqliteMigrationRunnerTests.cs`

- [ ] **Step 1: Write the failing migration tests**

Create `tests/NovelSpeaker.UnitTests/Persistence/SqliteMigrationRunnerTests.cs`:

```csharp
using Microsoft.Data.Sqlite;
using NovelSpeaker.Infrastructure.FileSystem;
using NovelSpeaker.Infrastructure.Persistence;

namespace NovelSpeaker.UnitTests.Persistence;

public sealed class SqliteMigrationRunnerTests
{
    [Fact]
    public async Task InitializeAsync_creates_schema_version_and_metadata_tables()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var directories = new LocalAppDataDirectoryProvider(root);
        var factory = new SqliteConnectionFactory(directories);
        var runner = new SqliteMigrationRunner(factory);
        var initializer = new StartupDatabaseInitializer(directories, runner);

        await initializer.InitializeAsync(CancellationToken.None);

        await using var connection = await factory.OpenConnectionAsync(CancellationToken.None);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN ('SchemaVersion', 'AppMetadata');";

        var count = Convert.ToInt32(await command.ExecuteScalarAsync(CancellationToken.None));
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task InitializeAsync_is_idempotent()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var directories = new LocalAppDataDirectoryProvider(root);
        var factory = new SqliteConnectionFactory(directories);
        var runner = new SqliteMigrationRunner(factory);
        var initializer = new StartupDatabaseInitializer(directories, runner);

        await initializer.InitializeAsync(CancellationToken.None);
        await initializer.InitializeAsync(CancellationToken.None);

        await using var connection = await factory.OpenConnectionAsync(CancellationToken.None);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT MAX(Version) FROM SchemaVersion;";

        var version = Convert.ToInt32(await command.ExecuteScalarAsync(CancellationToken.None));
        Assert.Equal(1, version);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run:

```bash
dotnet test tests/NovelSpeaker.UnitTests/NovelSpeaker.UnitTests.csproj --filter SqliteMigrationRunnerTests
```

Expected: fail because the persistence types do not exist yet.

- [ ] **Step 3: Implement the connection factory and migration runner**

Create `src/NovelSpeaker.Infrastructure/Persistence/SqliteConnectionFactory.cs`:

```csharp
using Microsoft.Data.Sqlite;
using NovelSpeaker.Application.Abstractions;

namespace NovelSpeaker.Infrastructure.Persistence;

/// <summary>
/// Opens SQLite connections against the application database file.
/// </summary>
public sealed class SqliteConnectionFactory : ISqliteConnectionFactory
{
    private readonly IAppDataDirectoryProvider _directories;

    public SqliteConnectionFactory(IAppDataDirectoryProvider directories)
    {
        _directories = directories;
    }

    public async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection($"Data Source={_directories.DatabasePath}");
        await connection.OpenAsync(cancellationToken);
        return connection;
    }
}
```

Create `src/NovelSpeaker.Infrastructure/Persistence/SqliteMigration.cs`:

```csharp
namespace NovelSpeaker.Infrastructure.Persistence;

internal sealed record SqliteMigration(int Version, string Sql);
```

Create `src/NovelSpeaker.Infrastructure/Persistence/SqliteMigrationRunner.cs`:

```csharp
using Microsoft.Data.Sqlite;
using NovelSpeaker.Application.Abstractions;

namespace NovelSpeaker.Infrastructure.Persistence;

/// <summary>
/// Applies explicit schema migrations to the local SQLite database.
/// </summary>
public sealed class SqliteMigrationRunner : IDatabaseInitializer
{
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
            """)
    ];

    private readonly ISqliteConnectionFactory _connectionFactory;

    public SqliteMigrationRunner(ISqliteConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await EnsureMigrationTableAsync(connection, cancellationToken);

        var currentVersion = await GetCurrentVersionAsync(connection, cancellationToken);

        foreach (var migration in Migrations.Where(m => m.Version > currentVersion))
        {
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = migration.Sql;
            await command.ExecuteNonQueryAsync(cancellationToken);

            var versionCommand = connection.CreateCommand();
            versionCommand.Transaction = transaction;
            versionCommand.CommandText = "INSERT INTO SchemaVersion (Version) VALUES ($version);";
            versionCommand.Parameters.AddWithValue("$version", migration.Version);
            await versionCommand.ExecuteNonQueryAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
    }

    private static async Task EnsureMigrationTableAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS SchemaVersion (
                Version INTEGER NOT NULL PRIMARY KEY
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<int> GetCurrentVersionAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(MAX(Version), 0) FROM SchemaVersion;";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }
}
```

Create `src/NovelSpeaker.Infrastructure/Persistence/StartupDatabaseInitializer.cs`:

```csharp
using NovelSpeaker.Application.Abstractions;

namespace NovelSpeaker.Infrastructure.Persistence;

/// <summary>
/// Ensures startup storage exists before schema initialization runs.
/// </summary>
public sealed class StartupDatabaseInitializer : IDatabaseInitializer
{
    private readonly IAppDataDirectoryProvider _directories;
    private readonly SqliteMigrationRunner _migrationRunner;

    public StartupDatabaseInitializer(
        IAppDataDirectoryProvider directories,
        SqliteMigrationRunner migrationRunner)
    {
        _directories = directories;
        _migrationRunner = migrationRunner;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await _directories.EnsureCreatedAsync(cancellationToken);
        await _migrationRunner.InitializeAsync(cancellationToken);
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run:

```bash
dotnet test tests/NovelSpeaker.UnitTests/NovelSpeaker.UnitTests.csproj --filter SqliteMigrationRunnerTests
```

Expected: PASS.

- [ ] **Step 5: Commit the migration infrastructure**

```bash
git add src/NovelSpeaker.Infrastructure/Persistence tests/NovelSpeaker.UnitTests/Persistence
git commit -m "feat: add sqlite startup migration infrastructure"
```

---

### Task 4: Wire dependency injection, logging, and the minimal WPF MVVM shell

**Files:**
- Create: `src/NovelSpeaker.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs`
- Create: `src/NovelSpeaker.App/ViewModels/MainWindowViewModel.cs`
- Create: `tests/NovelSpeaker.UnitTests/DependencyInjection/ServiceCollectionExtensionsTests.cs`
- Modify: `src/NovelSpeaker.App/App.xaml`
- Modify: `src/NovelSpeaker.App/App.xaml.cs`
- Modify: `src/NovelSpeaker.App/MainWindow.xaml`
- Modify: `src/NovelSpeaker.App/MainWindow.xaml.cs`

- [ ] **Step 1: Expand the service registration test so it fails for the right reason**

Create `tests/NovelSpeaker.UnitTests/DependencyInjection/ServiceCollectionExtensionsTests.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.App;
using NovelSpeaker.App.ViewModels;
using NovelSpeaker.Infrastructure.DependencyInjection;

namespace NovelSpeaker.UnitTests.DependencyInjection;

public sealed class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddNovelSpeakerInfrastructure_registers_core_services()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNovelSpeakerInfrastructure();
        services.AddNovelSpeakerDesktop();

        using var provider = services.BuildServiceProvider();

        Assert.IsType<MainWindowViewModel>(provider.GetRequiredService<MainWindowViewModel>());
        Assert.IsAssignableFrom<IAppDataDirectoryProvider>(provider.GetRequiredService<IAppDataDirectoryProvider>());
        Assert.IsAssignableFrom<IDatabaseInitializer>(provider.GetRequiredService<IDatabaseInitializer>());
        Assert.IsType<MainWindow>(provider.GetRequiredService<MainWindow>());
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run:

```bash
dotnet test tests/NovelSpeaker.UnitTests/NovelSpeaker.UnitTests.csproj --filter AddNovelSpeakerInfrastructure_registers_core_services
```

Expected: fail because DI extensions, `MainWindowViewModel`, and app registration do not exist yet.

- [ ] **Step 3: Implement DI extensions and the MVVM shell**

Create `src/NovelSpeaker.Infrastructure/DependencyInjection/ServiceCollectionExtensions.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using NovelSpeaker.Application.Abstractions;
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
        services.AddSingleton<SqliteMigrationRunner>();
        services.AddSingleton<IDatabaseInitializer, StartupDatabaseInitializer>();

        return services;
    }
}
```

Create `src/NovelSpeaker.App/ViewModels/MainWindowViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using NovelSpeaker.Application.Abstractions;

namespace NovelSpeaker.App.ViewModels;

/// <summary>
/// Exposes the minimal startup state for the application shell.
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    public MainWindowViewModel(IAppDataDirectoryProvider directories)
    {
        StatusText = "NovelSpeaker engineering foundation ready";
        DataDirectoryText = directories.RootDirectoryPath;
    }

    [ObservableProperty]
    private string statusText;

    [ObservableProperty]
    private string dataDirectoryText;
}
```

Update `src/NovelSpeaker.App/App.xaml`:

```xml
<Application x:Class="NovelSpeaker.App.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Application.Resources>
    </Application.Resources>
</Application>
```

Update `src/NovelSpeaker.App/App.xaml.cs`:

```csharp
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.App.ViewModels;
using NovelSpeaker.Infrastructure.DependencyInjection;

namespace NovelSpeaker.App;

/// <summary>
/// Configures the desktop composition root and starts the shell window.
/// </summary>
public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddDebug());
        services.AddNovelSpeakerInfrastructure();
        services.AddNovelSpeakerDesktop();

        _serviceProvider = services.BuildServiceProvider();

        var initializer = _serviceProvider.GetRequiredService<IDatabaseInitializer>();
        await initializer.InitializeAsync(CancellationToken.None);

        var window = _serviceProvider.GetRequiredService<MainWindow>();
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }
}
```

Update `src/NovelSpeaker.App/MainWindow.xaml`:

```xml
<Window x:Class="NovelSpeaker.App.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
        xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
        mc:Ignorable="d"
        Title="NovelSpeaker" Height="320" Width="640">
    <Grid Margin="24">
        <StackPanel>
            <TextBlock FontSize="24"
                       FontWeight="SemiBold"
                       Text="{Binding StatusText}" />
            <TextBlock Margin="0,16,0,0"
                       TextWrapping="Wrap"
                       Text="{Binding DataDirectoryText}" />
        </StackPanel>
    </Grid>
</Window>
```

Update `src/NovelSpeaker.App/MainWindow.xaml.cs`:

```csharp
using System.Windows;
using NovelSpeaker.App.ViewModels;

namespace NovelSpeaker.App;

/// <summary>
/// Hosts the minimal application shell.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
```

Add this file `src/NovelSpeaker.App/ServiceCollectionExtensions.cs`:

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
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>();
        return services;
    }
}
```

- [ ] **Step 4: Run the focused tests to verify they pass**

Run:

```bash
dotnet test tests/NovelSpeaker.UnitTests/NovelSpeaker.UnitTests.csproj --filter ServiceCollectionExtensionsTests
```

Expected: PASS.

- [ ] **Step 5: Commit the WPF composition root**

```bash
git add src/NovelSpeaker.App src/NovelSpeaker.Infrastructure/DependencyInjection tests/NovelSpeaker.UnitTests/DependencyInjection
git commit -m "feat: wire wpf startup with di and mvvm"
```

---

### Task 5: Add Windows CI, update backlog, and run full verification

**Files:**
- Create: `.github/workflows/ci.yml`
- Modify: `docs/11_TASK_BACKLOG.md`

- [ ] **Step 1: Write the failing expectation by running the repo-level verification commands**

Run:

```bash
dotnet build -c Release
dotnet test -c Release
```

Expected: build and test may still fail until CI config and final cleanups are added.

- [ ] **Step 2: Add the CI workflow and update Epic A checklist**

Create `.github/workflows/ci.yml`:

```yaml
name: CI

on:
  push:
    branches:
      - main
  pull_request:

jobs:
  build-and-test:
    runs-on: windows-latest

    steps:
      - name: Checkout
        uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 10.0.x

      - name: Restore
        run: dotnet restore

      - name: Build
        run: dotnet build -c Release --no-restore

      - name: Test
        run: dotnet test -c Release --no-build

      - name: Format
        run: dotnet format --verify-no-changes --no-restore
```

Update the `Epic A` section in `docs/11_TASK_BACKLOG.md` to:

```markdown
## Epic A：工程基础

- [x] 创建 .NET 10 WPF 解决方案。
- [x] 建立 Domain、Application、Infrastructure、App 和 Tests。
- [x] 配置 CommunityToolkit.Mvvm。
- [x] 配置依赖注入。
- [x] 配置 Microsoft.Extensions.Logging。
- [x] 建立应用数据目录服务。
- [x] 配置 Nullable 和分析器。
- [x] 建立数据库迁移基础设施。
- [x] 添加 CI 构建和测试。
```

- [ ] **Step 3: Run the full verification commands**

Run:

```bash
dotnet format --verify-no-changes
dotnet build -c Release
dotnet test -c Release
```

Expected: all commands succeed. If `dotnet format` fails because generated files need cleanup, fix formatting before repeating.

- [ ] **Step 4: Commit the CI and backlog updates**

```bash
git add .github/workflows/ci.yml docs/11_TASK_BACKLOG.md
git commit -m "ci: add windows build and test workflow"
```

- [ ] **Step 5: Prepare the handoff note**

Include all five required delivery points in the final report:

```text
- 修改了什么。
- 为什么这样设计。
- 添加或更新了哪些测试。
- 如何手动验证。
- 是否存在尚未解决的风险。
```

---

## Self-Review

### Spec coverage

- Layered solution structure: covered by Task 1.
- Application abstractions and app data directories: covered by Task 2.
- SQLite startup migration and idempotency: covered by Task 3.
- DI, logging, CommunityToolkit.Mvvm, and code-behind boundary: covered by Task 4.
- Windows CI and backlog completion: covered by Task 5.

### Placeholder scan

- No `TODO`, `TBD`, or deferred “handle later” steps remain.
- Each test-first task includes a concrete failing test, exact command, expected failure/pass condition, and implementation snippet.

### Type consistency

- `IAppDataDirectoryProvider`, `IDatabaseInitializer`, and `ISqliteConnectionFactory` are introduced once and reused consistently.
- `AddNovelSpeakerInfrastructure` and `AddNovelSpeakerDesktop` are defined in the same names used by tests and startup code.
