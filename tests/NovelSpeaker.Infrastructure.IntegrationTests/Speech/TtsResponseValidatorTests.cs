using NovelSpeaker.Application.Speech.Compilation;
using NovelSpeaker.Application.Speech.Execution;
using NovelSpeaker.Domain.Speech;
using NovelSpeaker.Infrastructure.FileSystem;
using NovelSpeaker.Infrastructure.Speech.Http;
using NovelSpeaker.TestKit.Common;
using Microsoft.Extensions.Logging;
using Xunit;

namespace NovelSpeaker.Infrastructure.IntegrationTests.Speech;

public sealed class TtsResponseValidatorTests
{
    [Fact]
    public async Task ValidateAsync_classifies_an_empty_success_response_separately()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var directories = new AppDataDirectoryProvider(root);
        await directories.EnsureCreatedAsync(CancellationToken.None);
        var validator = new TtsResponseValidator(
            new TemporaryAudioStore(directories),
            new AudioProbe());
        await using var response = new TtsTransportResponse(
            200,
            "audio/mpeg",
            new MemoryStream());

        var result = await validator.ValidateAsync(CreateRequest(), response, CancellationToken.None);

        Assert.Equal(TtsErrorKind.EmptyAudioResponse, result.Failure!.Kind);
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(directories.CacheDirectoryPath, "RuleTests")));
    }

    [Fact]
    public async Task ValidateAsync_classifies_terminal_http_statuses()
    {
        foreach (var (statusCode, expectedKind) in new[]
                 {
                     (401, TtsErrorKind.Unauthorized),
                     (403, TtsErrorKind.Unauthorized),
                     (429, TtsErrorKind.RateLimited),
                     (503, TtsErrorKind.ServerError)
                 })
        {
            var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            var directories = new AppDataDirectoryProvider(root);
            await directories.EnsureCreatedAsync(CancellationToken.None);
            var validator = new TtsResponseValidator(new TemporaryAudioStore(directories), new AudioProbe());
            await using var response = new TtsTransportResponse(
                statusCode,
                "application/json",
                new MemoryStream("{\"token\":\"secret\"}"u8.ToArray()),
                statusCode == 429 ? TimeSpan.FromSeconds(3) : null);

            var result = await validator.ValidateAsync(CreateRequest(), response, CancellationToken.None);

            Assert.Equal(expectedKind, result.Failure!.Kind);
            Assert.DoesNotContain("secret", result.Failure.ResponseSummary, StringComparison.Ordinal);
            Assert.Equal(statusCode == 429 ? TimeSpan.FromSeconds(3) : null, result.Failure.RetryAfter);
        }
    }

    [Fact]
    public async Task ValidateAsync_deletes_all_temporary_files_when_audio_decode_fails()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var directories = new AppDataDirectoryProvider(root);
        await directories.EnsureCreatedAsync(CancellationToken.None);
        var store = new TemporaryAudioStore(directories);
        var validator = new TtsResponseValidator(store, new AudioProbe());
        await using var response = new TtsTransportResponse(
            200,
            "audio/mpeg",
            new MemoryStream("not audio"u8.ToArray()));

        var result = await validator.ValidateAsync(CreateRequest(), response, CancellationToken.None);

        Assert.Equal(TtsErrorKind.AudioDecode, result.Failure!.Kind);
        var temporaryDirectory = Path.Combine(directories.CacheDirectoryPath, "RuleTests");
        Assert.Empty(Directory.EnumerateFiles(temporaryDirectory));
    }

    [Fact]
    public async Task ValidateAsync_returns_decodable_audio_and_removes_staging_file()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var directories = new AppDataDirectoryProvider(root);
        await directories.EnsureCreatedAsync(CancellationToken.None);
        var validator = new TtsResponseValidator(new TemporaryAudioStore(directories), new AudioProbe());
        var bytes = await File.ReadAllBytesAsync(
            Path.Combine(AppContext.BaseDirectory, "TestAssets", "Audio", "demo-tone.wav"));
        await using var response = new TtsTransportResponse(200, "audio/wav", new MemoryStream(bytes));

        var result = await validator.ValidateAsync(CreateRequest(), response, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("wav", result.Audio!.DetectedAudioFormat);
        Assert.True(File.Exists(result.Audio.FilePath));
        Assert.DoesNotContain(
            Directory.EnumerateFiles(Path.GetDirectoryName(result.Audio.FilePath)!),
            path => Path.GetExtension(path).Equals(".tmp", StringComparison.OrdinalIgnoreCase));
        File.Delete(result.Audio.FilePath);
    }

    [Fact]
    public async Task ValidateAsync_projects_copy_failure_as_safe_unknown_and_redacts_log()
    {
        const string token = "validator-token-9274";
        const string body = "validator-body-1385";
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var directories = new AppDataDirectoryProvider(root);
        await directories.EnsureCreatedAsync(CancellationToken.None);
        var logger = new CapturingLogger<TtsResponseValidator>();
        var validator = new TtsResponseValidator(
            new TemporaryAudioStore(directories),
            new AudioProbe(),
            logger);
        var request = CreateRequest() with
        {
            Url = new Uri($"https://example.com/tts?token={token}"),
            Headers = new Dictionary<string, string> { ["Authorization"] = $"Bearer {token}" },
            Body = new ParsedTtsRequestBody(ParsedTtsRequestBodyKind.Json, $"{{\"body\":\"{body}\"}}", null)
        };
        await using var response = new TtsTransportResponse(
            200,
            "audio/wav",
            new ThrowingReadStream($"read failed: {token}; body={body}"));

        var result = await validator.ValidateAsync(request, response, CancellationToken.None);

        Assert.Equal(TtsErrorKind.Unknown, result.Failure!.Kind);
        Assert.DoesNotContain(token, result.Failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(body, result.Failure.Message, StringComparison.Ordinal);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Null(entry.Exception);
        Assert.DoesNotContain(token, entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(body, entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidateAsync_propagates_copy_cancellation_for_execution_boundary()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var directories = new AppDataDirectoryProvider(root);
        await directories.EnsureCreatedAsync(CancellationToken.None);
        var validator = new TtsResponseValidator(new TemporaryAudioStore(directories), new AudioProbe());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await using var response = new TtsTransportResponse(200, "audio/wav", new MemoryStream([1]));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            validator.ValidateAsync(CreateRequest(), response, cancellation.Token));

        var temporaryDirectory = Path.Combine(directories.CacheDirectoryPath, "RuleTests");
        Assert.Empty(Directory.EnumerateFiles(temporaryDirectory));
    }

    [Fact]
    public async Task CreateCandidate_removes_partial_file_when_copy_fails()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var directories = new AppDataDirectoryProvider(root);
        await directories.EnsureCreatedAsync(CancellationToken.None);
        var operations = new PartialCopyThenThrowOperations();
        var store = new TemporaryAudioStore(directories, operations);
        var temporaryPath = Path.Combine(directories.CacheDirectoryPath, "RuleTests", "source.tmp");
        Directory.CreateDirectory(Path.GetDirectoryName(temporaryPath)!);
        File.WriteAllText(temporaryPath, "source");
        var candidatePath = Path.ChangeExtension(temporaryPath, "wav");

        Assert.Throws<IOException>(() => store.CreateCandidate(temporaryPath, "wav"));

        Assert.False(File.Exists(candidatePath));
    }

    private static ParsedTtsRequest CreateRequest() => new(
        7,
        "GET",
        new Uri("https://example.com/tts"),
        new Dictionary<string, string>(),
        ParsedTtsRequestBody.None,
        "audio/wav");

    private sealed class ThrowingReadStream(string message) : MemoryStream
    {
        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(new IOException(message));
    }

    private sealed class PartialCopyThenThrowOperations : ITemporaryAudioFileOperations
    {
        public void Copy(string sourcePath, string destinationPath)
        {
            File.WriteAllText(destinationPath, "partial");
            throw new IOException("copy failed");
        }

        public void Delete(string path)
        {
            TemporaryAudioStore.Delete(path);
        }
    }
}
