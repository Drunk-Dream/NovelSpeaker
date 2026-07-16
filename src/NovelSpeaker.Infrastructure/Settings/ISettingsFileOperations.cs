namespace NovelSpeaker.Infrastructure.Settings;

internal interface ISettingsFileOperations
{
    bool Exists(string path);

    Stream OpenRead(string path);

    Stream CreateForWrite(string path);

    void FlushToDisk(Stream stream);

    void Move(string sourcePath, string destinationPath, bool overwrite);

    void Delete(string path);
}

internal sealed class PhysicalSettingsFileOperations : ISettingsFileOperations
{
    public static PhysicalSettingsFileOperations Instance { get; } = new();

    public bool Exists(string path) => File.Exists(path);

    public Stream OpenRead(string path) => new FileStream(
        path,
        FileMode.Open,
        FileAccess.Read,
        FileShare.Read,
        bufferSize: 4096,
        FileOptions.Asynchronous | FileOptions.SequentialScan);

    public Stream CreateForWrite(string path) => new FileStream(
        path,
        FileMode.CreateNew,
        FileAccess.Write,
        FileShare.None,
        bufferSize: 4096,
        FileOptions.Asynchronous | FileOptions.WriteThrough);

    public void FlushToDisk(Stream stream)
    {
        if (stream is FileStream fileStream)
        {
            fileStream.Flush(flushToDisk: true);
        }
    }

    public void Move(string sourcePath, string destinationPath, bool overwrite) =>
        File.Move(sourcePath, destinationPath, overwrite);

    public void Delete(string path) => File.Delete(path);
}
