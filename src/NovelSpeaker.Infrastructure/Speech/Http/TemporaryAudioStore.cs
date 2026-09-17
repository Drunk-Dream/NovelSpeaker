using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Infrastructure.FileSystem;

namespace NovelSpeaker.Infrastructure.Speech.Http;

/// <summary>Owns temporary HTTP TTS response files and their cleanup.</summary>
public sealed class TemporaryAudioStore
{
    private readonly IAppDataDirectoryProvider _directories;
    private readonly IAppStoragePathResolver _pathResolver;
    private readonly ITemporaryAudioFileOperations _fileOperations;

    public TemporaryAudioStore(IAppDataDirectoryProvider directories)
        : this(directories, new TemporaryAudioFileOperations(), new AppStoragePathResolver(directories))
    {
    }

    internal TemporaryAudioStore(
        IAppDataDirectoryProvider directories,
        ITemporaryAudioFileOperations fileOperations)
        : this(directories, fileOperations, new AppStoragePathResolver(directories))
    {
    }

    internal TemporaryAudioStore(
        IAppDataDirectoryProvider directories,
        ITemporaryAudioFileOperations fileOperations,
        IAppStoragePathResolver pathResolver)
    {
        _directories = directories ?? throw new ArgumentNullException(nameof(directories));
        _fileOperations = fileOperations ?? throw new ArgumentNullException(nameof(fileOperations));
        _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
    }

    public async Task<string> WriteAsync(long ruleId, Stream content, CancellationToken cancellationToken)
    {
        var directoryPath = _pathResolver.ResolvePath(
            Path.Combine(_directories.CacheDirectoryPath, "RuleTests"));
        Directory.CreateDirectory(directoryPath);
        var path = _pathResolver.ResolvePath(
            Path.Combine(directoryPath, $"tts-{ruleId}-{Guid.NewGuid():N}.tmp"));
        try
        {
            await using var file = File.Create(path);
            await content.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
            return path;
        }
        catch
        {
            _fileOperations.Delete(path);
            throw;
        }
    }

    public string CreateCandidate(string temporaryPath, string extension)
    {
        temporaryPath = _pathResolver.ResolvePath(temporaryPath);
        var candidate = _pathResolver.ResolvePath(Path.ChangeExtension(temporaryPath, extension));
        _fileOperations.Delete(candidate);
        try
        {
            _fileOperations.Copy(temporaryPath, candidate);
            return candidate;
        }
        catch
        {
            _fileOperations.Delete(candidate);
            throw;
        }
    }

    internal IAsyncDisposable TransferOwnership(string path)
    {
        return new TemporaryAudioFileOwner(_pathResolver.ResolvePath(path), _fileOperations);
    }

    public static void Delete(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }
}

internal interface ITemporaryAudioFileOperations
{
    void Copy(string sourcePath, string destinationPath);

    void Delete(string path);
}

internal sealed class TemporaryAudioFileOperations : ITemporaryAudioFileOperations
{
    public void Copy(string sourcePath, string destinationPath)
    {
        File.Copy(sourcePath, destinationPath, overwrite: true);
    }

    public void Delete(string path)
    {
        TemporaryAudioStore.Delete(path);
    }
}

internal sealed class TemporaryAudioFileOwner(
    string path,
    ITemporaryAudioFileOperations fileOperations) : IAsyncDisposable
{
    private string? _path = path;

    public ValueTask DisposeAsync()
    {
        var ownedPath = Interlocked.Exchange(ref _path, null);
        if (ownedPath is not null)
        {
            fileOperations.Delete(ownedPath);
        }

        return ValueTask.CompletedTask;
    }
}
