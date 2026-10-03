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
    [DirectoryLinkFact]
    public async Task Temporary_store_rejects_a_rule_test_directory_link_before_writing_outside_the_root()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var directories = new AppDataDirectoryProvider(root);
        await directories.EnsureCreatedAsync(CancellationToken.None);
        var outside = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(outside);
        DirectoryLinkTestHelper.CreateDirectoryLink(
            Path.Combine(directories.CacheDirectoryPath, "RuleTests"),
            outside);
        var store = new TemporaryAudioStore(directories);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            store.WriteAsync(1, new MemoryStream([1, 2, 3]), CancellationToken.None));

        Assert.Empty(Directory.EnumerateFileSystemEntries(outside));
    }

    [Fact]
    public async Task Temporary_store_accepts_response_at_byte_limit_without_a_known_length()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var directories = new AppDataDirectoryProvider(root);
        await directories.EnsureCreatedAsync(CancellationToken.None);
        var store = new TemporaryAudioStore(directories);
        await using var content = new GeneratedContentStream(TemporaryAudioStore.MaximumResponseBytes);

        var path = await store.WriteAsync(1, content, CancellationToken.None);

        Assert.Equal(TemporaryAudioStore.MaximumResponseBytes, new FileInfo(path).Length);
        TemporaryAudioStore.Delete(path);
    }

    [Fact]
    public async Task Temporary_store_rejects_actual_bytes_over_limit_and_deletes_partial_file()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var directories = new AppDataDirectoryProvider(root);
        await directories.EnsureCreatedAsync(CancellationToken.None);
        var store = new TemporaryAudioStore(directories);
        await using var content = new GeneratedContentStream(TemporaryAudioStore.MaximumResponseBytes + 1);

        await Assert.ThrowsAsync<TtsAudioResponseTooLargeException>(() =>
            store.WriteAsync(1, content, CancellationToken.None));

        Assert.Empty(Directory.EnumerateFiles(Path.Combine(directories.CacheDirectoryPath, "RuleTests")));
    }

    [Fact]
    public async Task ValidateAsync_returns_stable_invalid_response_for_oversized_audio()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var directories = new AppDataDirectoryProvider(root);
        await directories.EnsureCreatedAsync(CancellationToken.None);
        var validator = new TtsResponseValidator(new TemporaryAudioStore(directories), new AudioProbe());
        await using var response = new TtsTransportResponse(
            200,
            "audio/mpeg",
            new GeneratedContentStream(TemporaryAudioStore.MaximumResponseBytes + 1));

        var result = await validator.ValidateAsync(CreateRequest(), response, CancellationToken.None);

        Assert.Equal(TtsErrorKind.InvalidResponse, result.Failure!.Kind);
        Assert.Equal("服务返回的音频超过允许大小，无法生成音频。", result.Failure.Message);
        Assert.Null(result.Failure.ResponseSummary);
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(directories.CacheDirectoryPath, "RuleTests")));
    }

    [Fact]
    public async Task Temporary_store_deletes_partial_file_when_cancelled_during_copy()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var directories = new AppDataDirectoryProvider(root);
        await directories.EnsureCreatedAsync(CancellationToken.None);
        var store = new TemporaryAudioStore(directories);
        using var cancellation = new CancellationTokenSource();
        await using var content = new GeneratedContentStream(TemporaryAudioStore.MaximumResponseBytes, cancellation);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.WriteAsync(1, content, cancellation.Token));

        Assert.Empty(Directory.EnumerateFiles(Path.Combine(directories.CacheDirectoryPath, "RuleTests")));
    }

    [Fact]
    public async Task Temporary_store_deletes_partial_file_when_file_write_fails()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var directories = new AppDataDirectoryProvider(root);
        await directories.EnsureCreatedAsync(CancellationToken.None);
        var store = new TemporaryAudioStore(directories, new WriteFailureOperations());
        await using var content = new GeneratedContentStream(128);

        await Assert.ThrowsAsync<IOException>(() => store.WriteAsync(1, content, CancellationToken.None));

        Assert.Empty(Directory.EnumerateFiles(Path.Combine(directories.CacheDirectoryPath, "RuleTests")));
    }

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
        public Stream Create(string path) => File.Create(path);

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

    private sealed class WriteFailureOperations : ITemporaryAudioFileOperations
    {
        public Stream Create(string path) => new PartialThenFailWriteStream(File.Create(path));

        public void Copy(string sourcePath, string destinationPath) => File.Copy(sourcePath, destinationPath);

        public void Delete(string path) => TemporaryAudioStore.Delete(path);
    }

    private sealed class PartialThenFailWriteStream(Stream inner) : Stream
    {
        private bool _failed;

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => inner.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_failed)
            {
                throw new IOException("simulated disk write failure");
            }

            _failed = true;
            await inner.WriteAsync(buffer[..Math.Min(buffer.Length, 4)], cancellationToken);
            throw new IOException("simulated disk write failure");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }

        public override ValueTask DisposeAsync() => inner.DisposeAsync();
    }

    private sealed class GeneratedContentStream(long totalBytes, CancellationTokenSource? cancelAfterFirstRead = null) : Stream
    {
        private long _remaining = totalBytes;
        private bool _cancelled;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_remaining == 0)
            {
                return ValueTask.FromResult(0);
            }

            var bytesRead = (int)Math.Min(buffer.Length, _remaining);
            buffer.Span[..bytesRead].Clear();
            _remaining -= bytesRead;
            if (!_cancelled && cancelAfterFirstRead is not null)
            {
                _cancelled = true;
                cancelAfterFirstRead.Cancel();
            }

            return ValueTask.FromResult(bytesRead);
        }
    }
}
