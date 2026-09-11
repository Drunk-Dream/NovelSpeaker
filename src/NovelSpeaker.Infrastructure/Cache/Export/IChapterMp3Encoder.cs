namespace NovelSpeaker.Infrastructure.Cache.Export;

internal interface IChapterMp3Encoder
{
    Task EncodeAsync(
        IReadOnlyList<string> sourceFilePaths,
        Stream destination,
        CancellationToken cancellationToken);
}
