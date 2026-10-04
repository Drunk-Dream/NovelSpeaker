using NovelSpeaker.Application.Abstractions;

namespace NovelSpeaker.Infrastructure.FileSystem;

/// <summary>
/// Gives temporary speech files a process-owned directory. The exclusive lease file
/// lets another process distinguish abandoned directories from files still in use.
/// </summary>
internal sealed class TemporarySpeechFileLease : IDisposable
{
    private const string LeaseFileName = ".owner-lock";
    private readonly IAppStoragePathResolver _pathResolver;
    private readonly FileStream _lease;

    public TemporarySpeechFileLease(
        IAppDataDirectoryProvider directories,
        IAppStoragePathResolver pathResolver)
    {
        ArgumentNullException.ThrowIfNull(directories);
        _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
        RootPath = _pathResolver.ResolvePath(Path.Combine(
            directories.CacheDirectoryPath,
            "TemporarySpeech",
            Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(RootPath);
        _lease = new FileStream(
            Path.Combine(RootPath, LeaseFileName),
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.Read | FileShare.Delete);
    }

    public string RootPath { get; }

    public string CreatePath(params string[] segments)
    {
        ArgumentNullException.ThrowIfNull(segments);
        var path = _pathResolver.ResolvePath(Path.Combine([RootPath, .. segments]));
        var relativePath = Path.GetRelativePath(RootPath, path);
        if (relativePath == "." || relativePath == ".." ||
            relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidDataException("临时语音文件路径超出应用管理目录。");
        }

        return path;
    }

    public void DeleteAbandonedDirectories(CancellationToken cancellationToken)
    {
        var leaseRoot = _pathResolver.ResolvePath(Path.Combine(
            Path.GetDirectoryName(Path.GetDirectoryName(RootPath)!)!,
            "TemporarySpeech"));
        if (!Directory.Exists(leaseRoot))
        {
            return;
        }

        foreach (var directory in Directory.EnumerateDirectories(leaseRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string resolvedDirectory;
            string lockPath;
            try
            {
                resolvedDirectory = _pathResolver.ResolvePath(directory);
                lockPath = _pathResolver.ResolvePath(Path.Combine(resolvedDirectory, LeaseFileName));
            }
            catch (InvalidDataException)
            {
                continue;
            }

            if (string.Equals(resolvedDirectory, RootPath, OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal))
            {
                continue;
            }

            if (!File.Exists(lockPath))
            {
                continue;
            }

            try
            {
                using (new FileStream(lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                }

                Directory.Delete(resolvedDirectory, recursive: true);
            }
            catch (IOException)
            {
                // A live process owns this directory, or another cleaner won the race.
            }
            catch (UnauthorizedAccessException)
            {
                // Cleanup is best effort; do not disturb speech generation.
            }
        }
    }

    public void Dispose() => _lease.Dispose();
}
