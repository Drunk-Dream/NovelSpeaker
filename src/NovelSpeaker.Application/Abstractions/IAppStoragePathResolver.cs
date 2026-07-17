namespace NovelSpeaker.Application.Abstractions;

/// <summary>
/// Converts application-owned storage keys and legacy paths without allowing access outside the data root.
/// </summary>
public interface IAppStoragePathResolver
{
    string ResolvePath(string storageKeyOrLegacyPath);

    string GetStorageKey(string appOwnedPath);
}
