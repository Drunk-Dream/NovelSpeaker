using NovelSpeaker.Application.Abstractions;

namespace NovelSpeaker.Infrastructure.FileSystem;

/// <summary>
/// Resolves storage paths under one canonical application root and rejects reparse-point traversal.
/// </summary>
public sealed class AppStoragePathResolver : IAppStoragePathResolver
{
    private readonly AppDataStorageTrustBoundary _trustBoundary;

    public AppStoragePathResolver(IAppDataDirectoryProvider directories)
    {
        ArgumentNullException.ThrowIfNull(directories);
        _trustBoundary = new AppDataStorageTrustBoundary(directories.RootDirectoryPath);
    }

    public string ResolvePath(string storageKeyOrLegacyPath) =>
        _trustBoundary.ResolvePath(storageKeyOrLegacyPath);

    public string GetStorageKey(string appOwnedPath) =>
        _trustBoundary.GetStorageKey(appOwnedPath);
}
