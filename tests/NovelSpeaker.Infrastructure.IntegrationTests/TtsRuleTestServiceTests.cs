using Microsoft.Extensions.Logging;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Speech;
using NovelSpeaker.Application.Speech.Compilation;
using NovelSpeaker.Application.Speech.Execution;
using NovelSpeaker.Application.Speech.Rules;
using NovelSpeaker.Application.Speech.Testing;
using NovelSpeaker.Domain.Speech;
using NovelSpeaker.Infrastructure.Speech.Http;
using NovelSpeaker.TestKit.Common;
using Xunit;

namespace NovelSpeaker.Infrastructure.IntegrationTests;

public sealed class TtsRuleTestServiceTests
{
    [Fact]
    public async Task TestAsync_projects_safe_normalization_failure_and_redacts_log()
    {
        const string token = "fixture-token-1826";
        const string body = "fixture-body-3047";
        const string novelText = "fixture-novel-text-5268";
        var input = CreateInput(token, body, novelText);
        var logger = new CapturingLogger<TtsRuleTestFailureReporter>();
        await using var service = CreateService(
            input.Editor,
            logger,
            normalizer: new ThrowingNormalizer(
                new FormatException($"Authorization=Bearer {token}; body={body}; text={novelText}")));

        var result = await service.TestAsync(input, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(TtsErrorKind.InvalidRule, result.ErrorKind);
        Assert.Equal("规则模板格式无效，请检查规则后重试。", result.Message);
        Assert.Null(result.Preview);
        AssertSafe(result.Message, token, body, novelText);
        AssertSafeLog(logger, token, body, novelText);
    }

    [Fact]
    public async Task TestAsync_projects_safe_local_playback_failure_and_redacts_log()
    {
        const string token = "fixture-token-7419";
        const string body = "fixture-body-8530";
        const string novelText = "fixture-novel-text-9641";
        var input = CreateInput(token, body, novelText);
        var logger = new CapturingLogger<TtsRuleTestFailureReporter>();
        var player = new FakeAudioPlayer
        {
            LoadException = new InvalidOperationException(
                $"Cookie={token}; LoginInfo={body}; text={novelText}")
        };
        await using var service = CreateService(input.Editor, logger, player: player);

        var result = await service.TestAsync(input, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(TtsErrorKind.AudioDecode, result.ErrorKind);
        Assert.Equal("音频已下载，但本地播放失败：本地音频播放失败，请重试。", result.Message);
        AssertSafe(result.Message, token, body, novelText);
        AssertSafeLog(logger, token, body, novelText);
    }

    [Fact]
    public async Task TestAsync_propagates_playback_cancellation_without_error_log()
    {
        var input = CreateInput("fixture-token", "fixture-body", "fixture-text");
        var logger = new CapturingLogger<TtsRuleTestFailureReporter>();
        var player = new FakeAudioPlayer { LoadException = new OperationCanceledException() };
        await using var service = CreateService(input.Editor, logger, player: player);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.TestAsync(input, CancellationToken.None));

        Assert.DoesNotContain(logger.Entries, entry => entry.Level == LogLevel.Error);
    }

    [Fact]
    public async Task TestAsync_executes_editor_draft_without_saving_it()
    {
        var input = CreateInput("fixture-token", "fixture-body", "fixture-text");
        var editor = new FakeRuleEditorUseCase(input.Editor);
        var compiler = new SuccessfulCompiler();
        var httpClient = new SuccessfulHttpClient();
        await using var service = new TtsRuleTestService(
            editor,
            compiler,
            httpClient,
            new FakeAudioPlayerFactory(new FakeAudioPlayer()));

        var result = await service.TestAsync(input, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, editor.SaveCallCount);
        Assert.Equal(1, compiler.CallCount);
        Assert.Equal(1, httpClient.CallCount);
    }

    [Fact]
    public async Task DisposeAsync_disposes_its_independent_player_once()
    {
        var input = CreateInput("fixture-token", "fixture-body", "fixture-text");
        var player = new FakeAudioPlayer();
        var factory = new FakeAudioPlayerFactory(player);
        var service = new TtsRuleTestService(
            new FakeRuleEditorUseCase(input.Editor),
            new SuccessfulCompiler(),
            new SuccessfulHttpClient(),
            factory);

        await service.DisposeAsync();
        await service.DisposeAsync();

        Assert.Equal(1, factory.CreateCallCount);
        Assert.Equal(1, player.DisposeCallCount);
    }

    [Fact]
    public async Task TestAsync_failed_playback_disposes_downloaded_audio()
    {
        var input = CreateInput("fixture-token", "fixture-body", "fixture-text");
        var audioOwner = new TrackingAudioOwner();
        var httpClient = new SuccessfulHttpClient(audioOwner);
        await using var service = new TtsRuleTestService(
            new FakeRuleEditorUseCase(input.Editor),
            new SuccessfulCompiler(),
            httpClient,
            new FakeAudioPlayerFactory(new FakeAudioPlayer
            {
                LoadException = new InvalidOperationException("load failed")
            }));

        var result = await service.TestAsync(input, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(1, audioOwner.DisposeCallCount);
    }

    [Fact]
    public async Task TestAsync_replacement_disposes_previous_audio_and_service_disposes_current()
    {
        var input = CreateInput("fixture-token", "fixture-body", "fixture-text");
        var firstOwner = new TrackingAudioOwner();
        var secondOwner = new TrackingAudioOwner();
        var httpClient = new SuccessfulHttpClient(firstOwner, secondOwner);
        var service = new TtsRuleTestService(
            new FakeRuleEditorUseCase(input.Editor),
            new SuccessfulCompiler(),
            httpClient,
            new FakeAudioPlayerFactory(new FakeAudioPlayer()));

        Assert.True((await service.TestAsync(input, CancellationToken.None)).IsSuccess);
        Assert.True((await service.TestAsync(input, CancellationToken.None)).IsSuccess);

        Assert.Equal(1, firstOwner.DisposeCallCount);
        Assert.Equal(0, secondOwner.DisposeCallCount);

        await service.DisposeAsync();

        Assert.Equal(1, secondOwner.DisposeCallCount);
    }

    private static TtsRuleTestService CreateService(
        TtsRuleEditorModel editor,
        CapturingLogger<TtsRuleTestFailureReporter> logger,
        ITtsRuleNormalizer? normalizer = null,
        FakeAudioPlayer? player = null)
    {
        return new TtsRuleTestService(
            new FakeRuleEditorUseCase(editor),
            new SuccessfulCompiler(),
            new SuccessfulHttpClient(),
            new FakeAudioPlayerFactory(player ?? new FakeAudioPlayer()),
            normalizer,
            failureReporter: new TtsRuleTestFailureReporter(logger));
    }

    private static TtsRuleDraftTestInput CreateInput(string token, string body, string novelText)
    {
        var editor = new TtsRuleEditorModel(
            null,
            "Fixture rule",
            true,
            $"https://example.com/tts?token={token}",
            "audio/mpeg",
            null,
            null,
            [new TtsRuleEditorKeyValue("Authorization", $"Bearer {token}")],
            new TtsRuleRequestOptionsEditor("POST", body));
        return new TtsRuleDraftTestInput(editor, novelText, 10);
    }

    private static void AssertSafe(string value, params string[] secrets)
    {
        foreach (var secret in secrets)
        {
            Assert.DoesNotContain(secret, value, StringComparison.Ordinal);
        }
    }

    private static void AssertSafeLog(CapturingLogger<TtsRuleTestFailureReporter> logger, params string[] secrets)
    {
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Null(entry.Exception);
        AssertSafe(entry.Message, secrets);
    }

    private sealed class ThrowingNormalizer(Exception exception) : ITtsRuleNormalizer
    {
        public NormalizedHttpTtsRule Normalize(HttpTtsRule rule) => throw exception;
    }

    private sealed class SuccessfulCompiler : ITtsRequestCompiler
    {
        public int CallCount { get; private set; }

        public Task<TtsRequestCompilationResult> CompileAsync(
            NormalizedHttpTtsRule rule,
            TtsRuleContext context,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new TtsRequestCompilationResult(
                new ParsedTtsRequest(
                    rule.RuleId,
                    "GET",
                    new Uri("https://example.com/tts"),
                    new Dictionary<string, string>(),
                    ParsedTtsRequestBody.None,
                    "audio/mpeg"),
                new TtsRequestPreview("GET", "https://example.com/tts", null, null, "audio/mpeg"),
                [],
                null));
        }
    }

    private sealed class SuccessfulHttpClient : IHttpTtsClient
    {
        private readonly Queue<IAsyncDisposable?> _owners;

        public SuccessfulHttpClient(params IAsyncDisposable[] owners)
        {
            _owners = new Queue<IAsyncDisposable?>(owners);
        }

        public int CallCount { get; private set; }

        public Task<TtsHttpExecutionResult> ExecuteAsync(
            ParsedTtsRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new TtsHttpExecutionResult(
                new TtsAudioResponse(
                    "fixture.mp3",
                    200,
                    "audio/mpeg",
                    "mp3",
                    _owners.Count > 0 ? _owners.Dequeue() : null),
                null));
        }
    }

    private sealed class TrackingAudioOwner : IAsyncDisposable
    {
        public int DisposeCallCount { get; private set; }

        public ValueTask DisposeAsync()
        {
            DisposeCallCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeAudioPlayerFactory(FakeAudioPlayer player) : IAudioPlayerFactory
    {
        public int CreateCallCount { get; private set; }

        public IAudioPlayer Create()
        {
            CreateCallCount++;
            return player;
        }
    }

    private sealed class FakeAudioPlayer : IAudioPlayer
    {
        public Exception? LoadException { get; init; }
        public int DisposeCallCount { get; private set; }
        public PlaybackState State => PlaybackState.Stopped;
        public TimeSpan Position => TimeSpan.Zero;
        public TimeSpan Duration => TimeSpan.Zero;
        public double Volume { get; set; } = PlaybackVolume.Default;
        public event EventHandler? PlaybackCompleted { add { } remove { } }
        public event EventHandler<PlaybackErrorEventArgs>? PlaybackFailed { add { } remove { } }

        public Task LoadAsync(string filePath, CancellationToken cancellationToken) =>
            LoadException is null ? Task.CompletedTask : Task.FromException(LoadException);

        public void Play() { }
        public void Pause() { }
        public void Stop() { }
        public void Seek(TimeSpan position) { }
        public ValueTask DisposeAsync()
        {
            DisposeCallCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeRuleEditorUseCase(TtsRuleEditorModel editor) : ITtsRuleEditorUseCase
    {
        public int SaveCallCount { get; private set; }

        public Task<TtsRuleDraftPreparationResult> PrepareDraftAsync(
            TtsRuleEditorModel model,
            CancellationToken cancellationToken)
        {
            var now = DateTimeOffset.Parse("2025-01-01T00:00:00Z");
            var candidate = new HttpTtsRule(
                editor.Id ?? 0,
                editor.Name,
                editor.Url,
                editor.ContentType,
                editor.ConcurrentRate,
                editor.Headers.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase),
                editor.RequestOptions.Method,
                editor.RequestOptions.Body,
                false,
                editor.LastUpdateTime,
                editor.IsEnabled,
                null,
                now,
                now);
            return Task.FromResult(new TtsRuleDraftPreparationResult(
                new TtsRuleValidationResult(true, [], editor),
                candidate));
        }

        public Task<TtsRuleValidationResult> ValidateEditorAsync(
            TtsRuleEditorModel model,
            CancellationToken cancellationToken) =>
            Task.FromResult(new TtsRuleValidationResult(true, [], editor));

        public Task<string> ExportEditorJsonAsync(TtsRuleEditorModel model, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<TtsRuleEditorModel?> GetEditorAsync(long ruleId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<HttpTtsRule> SaveEditorAsync(TtsRuleEditorModel model, CancellationToken cancellationToken)
        {
            SaveCallCount++;
            throw new NotSupportedException();
        }

        public Task SetRuleEnabledAsync(long ruleId, bool isEnabled, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
