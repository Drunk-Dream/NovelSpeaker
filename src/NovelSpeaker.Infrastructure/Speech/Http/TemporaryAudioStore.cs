using NovelSpeaker.Application.Cache.Audio;
using NovelSpeaker.Application.Speech.Execution;
using NovelSpeaker.Application.Speech.Providers;
using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.Infrastructure.FileSystem;

namespace NovelSpeaker.Infrastructure.Speech.Http;

/// <summary>Owns temporary HTTP TTS response files and their cleanup.</summary>
public sealed class TemporaryAudioStore : IGeneratedAudioFileStore
{
    internal const long MaximumResponseBytes = 64L * 1024 * 1024;

    private readonly IAppStoragePathResolver _pathResolver;
    private readonly ITemporaryAudioFileOperations _fileOperations;
    private readonly TemporarySpeechFileLease _temporaryFiles;

    public TemporaryAudioStore(IAppDataDirectoryProvider directories)
        : this(
            directories,
            new TemporaryAudioFileOperations(),
            new AppStoragePathResolver(directories),
            new TemporarySpeechFileLease(directories, new AppStoragePathResolver(directories)))
    {
    }

    internal TemporaryAudioStore(
        IAppDataDirectoryProvider directories,
        ITemporaryAudioFileOperations fileOperations)
        : this(
            directories,
            fileOperations,
            new AppStoragePathResolver(directories),
            new TemporarySpeechFileLease(directories, new AppStoragePathResolver(directories)))
    {
    }

    internal TemporaryAudioStore(
        IAppDataDirectoryProvider directories,
        ITemporaryAudioFileOperations fileOperations,
        IAppStoragePathResolver pathResolver)
        : this(
            directories,
            fileOperations,
            pathResolver,
            new TemporarySpeechFileLease(directories, pathResolver))
    {
    }

    internal TemporaryAudioStore(
        IAppDataDirectoryProvider directories,
        ITemporaryAudioFileOperations fileOperations,
        IAppStoragePathResolver pathResolver,
        TemporarySpeechFileLease temporaryFiles)
    {
        ArgumentNullException.ThrowIfNull(directories);
        _fileOperations = fileOperations ?? throw new ArgumentNullException(nameof(fileOperations));
        _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
        _temporaryFiles = temporaryFiles ?? throw new ArgumentNullException(nameof(temporaryFiles));
    }

    public async Task<TtsAudioResponse> WriteAsync(ProviderSynthesisResult audio, CancellationToken cancellationToken)
    {
        var path = await WriteAsync(0, audio.Audio!, cancellationToken).ConfigureAwait(false);
        try
        {
            var extension = audio.AudioFormat?.ToLowerInvariant() switch
            {
                "mp3" => ".mp3",
                "wav" => ".wav",
                _ => ".audio"
            };
            var candidate = CreateCandidate(path, extension);
            return new TtsAudioResponse(candidate, 200, audio.ContentType, audio.AudioFormat, TransferOwnership(candidate));
        }
        finally { Delete(path); }
    }

    public async Task<string> WriteAsync(long ruleId, Stream content, CancellationToken cancellationToken)
    {
        var path = _temporaryFiles.CreatePath("RuleTests", $"tts-{ruleId}-{Guid.NewGuid():N}.tmp");
        var directoryPath = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directoryPath);
        try
        {
            await using var file = _fileOperations.Create(path);
            var buffer = new byte[81920];
            long totalBytes = 0;
            while (true)
            {
                var remainingBytes = MaximumResponseBytes - totalBytes;
                var readBuffer = buffer.AsMemory(0, (int)Math.Min(buffer.Length, remainingBytes + 1));
                var bytesRead = await content.ReadAsync(readBuffer, cancellationToken).ConfigureAwait(false);
                if (bytesRead == 0)
                {
                    break;
                }

                if (bytesRead > remainingBytes)
                {
                    throw new TtsAudioResponseTooLargeException();
                }

                await file.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
                totalBytes += bytesRead;
            }

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
    Stream Create(string path);

    void Copy(string sourcePath, string destinationPath);

    void Delete(string path);
}

internal sealed class TemporaryAudioFileOperations : ITemporaryAudioFileOperations
{
    public Stream Create(string path) => File.Create(path);

    public void Copy(string sourcePath, string destinationPath)
    {
        File.Copy(sourcePath, destinationPath, overwrite: true);
    }

    public void Delete(string path)
    {
        TemporaryAudioStore.Delete(path);
    }
}

internal sealed class TtsAudioResponseTooLargeException()
    : IOException("HTTP TTS audio response exceeded the configured size limit.");

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
