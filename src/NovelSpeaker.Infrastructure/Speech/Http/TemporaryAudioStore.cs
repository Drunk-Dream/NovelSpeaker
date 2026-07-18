using NovelSpeaker.Application.Abstractions;

namespace NovelSpeaker.Infrastructure.Speech.Http;

/// <summary>Owns temporary HTTP TTS response files and their cleanup.</summary>
public sealed class TemporaryAudioStore
{
    private readonly string _directoryPath;
    private readonly ITemporaryAudioFileOperations _fileOperations;

    public TemporaryAudioStore(IAppDataDirectoryProvider directories)
        : this(directories, new TemporaryAudioFileOperations())
    {
    }

    internal TemporaryAudioStore(
        IAppDataDirectoryProvider directories,
        ITemporaryAudioFileOperations fileOperations)
    {
        _directoryPath = Path.Combine(directories.CacheDirectoryPath, "RuleTests");
        _fileOperations = fileOperations;
    }

    public async Task<string> WriteAsync(long ruleId, Stream content, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_directoryPath);
        var path = Path.Combine(_directoryPath, $"tts-{ruleId}-{Guid.NewGuid():N}.tmp");
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
        var candidate = Path.ChangeExtension(temporaryPath, extension);
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
