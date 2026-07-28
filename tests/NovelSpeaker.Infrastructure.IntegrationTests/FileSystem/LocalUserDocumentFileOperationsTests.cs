using NovelSpeaker.Infrastructure.FileSystem;
using NovelSpeaker.TestKit.Common;
using Xunit;

namespace NovelSpeaker.Infrastructure.IntegrationTests.FileSystem;

public sealed class LocalUserDocumentFileOperationsTests
{
    [Fact]
    public async Task Metadata_and_text_operations_round_trip_selected_file()
    {
        using var directory = new TemporaryDirectory();
        Directory.CreateDirectory(directory.Path);
        var filePath = Path.Combine(directory.Path, "rule.json");
        var operations = new LocalUserDocumentFileOperations();

        await operations.WriteTextAsync(filePath, """{"name":"demo"}""", CancellationToken.None);
        var metadata = await operations.GetMetadataAsync(filePath, CancellationToken.None);
        var content = await operations.ReadTextAsync(filePath, CancellationToken.None);

        Assert.NotNull(metadata);
        Assert.Equal("rule.json", metadata.FileName);
        Assert.Equal(".json", metadata.Extension);
        Assert.True(metadata.Length > 0);
        Assert.Equal("""{"name":"demo"}""", content);
    }

    [Fact]
    public async Task Metadata_returns_null_for_directory_or_missing_file()
    {
        using var directory = new TemporaryDirectory();
        Directory.CreateDirectory(directory.Path);
        var operations = new LocalUserDocumentFileOperations();

        Assert.Null(await operations.GetMetadataAsync(directory.Path, CancellationToken.None));
        Assert.Null(await operations.GetMetadataAsync(
            Path.Combine(directory.Path, "missing.txt"),
            CancellationToken.None));
    }

    [Fact]
    public async Task Operations_honor_cancellation()
    {
        using var directory = new TemporaryDirectory();
        Directory.CreateDirectory(directory.Path);
        var filePath = Path.Combine(directory.Path, "rule.json");
        await File.WriteAllTextAsync(filePath, "{}");
        var operations = new LocalUserDocumentFileOperations();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => operations.GetMetadataAsync(filePath, cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => operations.ReadTextAsync(filePath, cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => operations.WriteTextAsync(filePath, "{}", cancellation.Token));
    }
}
