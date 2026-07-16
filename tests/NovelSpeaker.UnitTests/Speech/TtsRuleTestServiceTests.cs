using Microsoft.Extensions.Logging;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Speech;
using NovelSpeaker.Application.Speech.Compilation;
using NovelSpeaker.Application.Speech.Execution;
using NovelSpeaker.Application.Speech.Rules;
using NovelSpeaker.Domain.Speech;
using NovelSpeaker.Infrastructure.Speech.Http;
using NovelSpeaker.UnitTests.Common;
using Xunit;

namespace NovelSpeaker.UnitTests.Speech;

public sealed class TtsRuleTestServiceTests
{
    [Fact]
    public async Task TestAsync_projects_safe_normalization_failure_and_redacts_log()
    {
        const string token = "fixture-token-1826";
        const string body = "fixture-body-3047";
        const string novelText = "fixture-novel-text-5268";
        var input = CreateInput(token, body, novelText);
        var logger = new CapturingLogger<TtsRuleTestService>();
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
        var logger = new CapturingLogger<TtsRuleTestService>();
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
        var logger = new CapturingLogger<TtsRuleTestService>();
        var player = new FakeAudioPlayer { LoadException = new OperationCanceledException() };
        await using var service = CreateService(input.Editor, logger, player: player);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.TestAsync(input, CancellationToken.None));

        Assert.DoesNotContain(logger.Entries, entry => entry.Level == LogLevel.Error);
    }

    private static TtsRuleTestService CreateService(
        TtsRuleEditorModel editor,
        CapturingLogger<TtsRuleTestService> logger,
        ITtsRuleNormalizer? normalizer = null,
        FakeAudioPlayer? player = null)
    {
        return new TtsRuleTestService(
            new FakeRuleLibraryService(editor),
            new SuccessfulCompiler(),
            new SuccessfulHttpClient(),
            new FakeAudioPlayerFactory(player ?? new FakeAudioPlayer()),
            normalizer,
            logger: logger);
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

    private static void AssertSafeLog(CapturingLogger<TtsRuleTestService> logger, params string[] secrets)
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
        public Task<TtsRequestCompilationResult> CompileAsync(
            NormalizedHttpTtsRule rule,
            TtsRuleContext context,
            CancellationToken cancellationToken)
        {
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
        public Task<TtsHttpExecutionResult> ExecuteAsync(
            ParsedTtsRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new TtsHttpExecutionResult(
                new TtsAudioResponse("fixture.mp3", 200, "audio/mpeg", "mp3"),
                null));
    }

    private sealed class FakeAudioPlayerFactory(FakeAudioPlayer player) : IAudioPlayerFactory
    {
        public IAudioPlayer Create() => player;
    }

    private sealed class FakeAudioPlayer : IAudioPlayer
    {
        public Exception? LoadException { get; init; }
        public PlaybackState State => PlaybackState.Stopped;
        public TimeSpan Position => TimeSpan.Zero;
        public TimeSpan Duration => TimeSpan.Zero;
        public event EventHandler? PlaybackCompleted { add { } remove { } }
        public event EventHandler<PlaybackErrorEventArgs>? PlaybackFailed { add { } remove { } }

        public Task LoadAsync(string filePath, CancellationToken cancellationToken) =>
            LoadException is null ? Task.CompletedTask : Task.FromException(LoadException);

        public void Play() { }
        public void Pause() { }
        public void Stop() { }
        public void Seek(TimeSpan position) { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeRuleLibraryService(TtsRuleEditorModel editor) : ITtsRuleLibraryService
    {
        public Task<TtsRuleValidationResult> ValidateEditorAsync(
            TtsRuleEditorModel model,
            CancellationToken cancellationToken) =>
            Task.FromResult(new TtsRuleValidationResult(true, [], editor));

        public Task<IReadOnlyList<TtsRuleSummary>> GetRulesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<TtsRuleImportPreview> CreateImportPreviewAsync(string jsonText, string sourceDescription, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<TtsRuleImportResult> ImportJsonTextAsync(string jsonText, string sourceDescription, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<TtsRuleImportResult> ImportAsync(TtsRuleImportPreview preview, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string?> ExportRuleJsonAsync(long ruleId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string> ExportEditorJsonAsync(TtsRuleEditorModel model, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<TtsRuleEditorModel?> GetEditorAsync(long ruleId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<HttpTtsRule> SaveEditorAsync(TtsRuleEditorModel model, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<TtsRuleProtectionInfo> GetRuleProtectionAsync(long ruleId, TtsRuleMutationAction action, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<TtsRuleMutationResult> ApplyRuleMutationAsync(TtsRuleMutationDecision decision, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SelectRuleAsync(long? ruleId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SetRuleEnabledAsync(long ruleId, bool isEnabled, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteRuleAsync(long ruleId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
