namespace NovelSpeaker.UnitTests.Common;

internal sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory(string namePrefix = "NovelSpeakerTests")
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            namePrefix,
            Guid.NewGuid().ToString("N"));
    }

    public string Path { get; }

    public void Dispose()
    {
        if (System.IO.Directory.Exists(Path))
        {
            System.IO.Directory.Delete(Path, recursive: true);
        }
    }
}
