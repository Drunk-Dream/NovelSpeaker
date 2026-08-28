namespace NovelSpeaker.Infrastructure.FileSystem;

/// <summary>
/// Selects the single application data root from process environment and runtime paths.
/// </summary>
public sealed class AppDataRootResolver
{
    public const string DataRootEnvironmentVariable = "NOVELSPEAKER_DATA_ROOT";
    public const string EnvironmentEnvironmentVariable = "NOVELSPEAKER_ENVIRONMENT";
    public const string DevelopmentEnvironmentName = "Development";
    public const string DevelopmentDirectoryName = "NovelSpeaker.Dev";
    public const string ProductionDirectoryName = "Data";

    private readonly string _applicationBaseDirectory;
    private readonly string _localAppDataDirectory;
    private readonly Func<string, string?> _environmentVariableReader;

    public AppDataRootResolver()
        : this(
            AppContext.BaseDirectory,
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetEnvironmentVariable)
    {
    }

    public AppDataRootResolver(
        string applicationBaseDirectory,
        string localAppDataDirectory,
        Func<string, string?>? environmentVariableReader = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationBaseDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(localAppDataDirectory);

        _applicationBaseDirectory = Path.GetFullPath(applicationBaseDirectory);
        _localAppDataDirectory = Path.GetFullPath(localAppDataDirectory);
        _environmentVariableReader = environmentVariableReader ?? Environment.GetEnvironmentVariable;
    }

    public string ResolveRootDirectoryPath()
    {
        var explicitRoot = _environmentVariableReader(DataRootEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(explicitRoot))
        {
            return Path.GetFullPath(explicitRoot);
        }

        var environment = _environmentVariableReader(EnvironmentEnvironmentVariable);
        var root = string.Equals(environment, DevelopmentEnvironmentName, StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(_localAppDataDirectory, DevelopmentDirectoryName)
            : Path.Combine(_applicationBaseDirectory, ProductionDirectoryName);

        return Path.GetFullPath(root);
    }
}
