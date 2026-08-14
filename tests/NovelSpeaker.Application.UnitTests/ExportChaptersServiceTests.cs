using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Cache;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Playback.Cache;
using NovelSpeaker.Application.Playback.Export;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Application.Speech.Compilation;
using NovelSpeaker.Domain.Books;
using NovelSpeaker.Domain.Settings;
using NovelSpeaker.TestKit.Speech;
using Xunit;

namespace NovelSpeaker.Application.UnitTests;

public sealed class ExportChaptersServiceTests
{
    [Fact]
    public async Task ExportAsync_uses_persisted_plan_order_and_hashes_without_reading_content_or_executing_regex()
    {
        var metadata = CreateMetadata();
        var planStore = new FakeChapterSpeechPlanStore();
        planStore.Plans["chapter-0"] = CreatePlan(
            "chapter-0",
            [
                CreatePlanSegment(1, 10, "正文乙。"),
                CreatePlanSegment(0, 0, "正文甲。")
            ]);
        var writer = new FakeChapterMp3ExportWriter();
        var service = CreateService(metadata, planStore, writer, AppSettings.Default with
        {
            DefaultSpeakSpeed = 12,
            SelectedTtsRuleId = 7
        });

        var result = await service.ExportAsync(
            new ExportChaptersRequest("book-1", [0], @"D:\exports"),
            CancellationToken.None);

        Assert.Equal(ExportChaptersStatus.Succeeded, result.Status);
        Assert.NotNull(writer.LastBatch);
        var plan = Assert.Single(writer.LastBatch!.Chapters);
        Assert.Equal(
            [
                AudioCacheKey.FromSpeechTextHash(
                    "chapter-0",
                    StableSpeechSegmentIdentity.Body(0, 4),
                    Fingerprint.Sha256("正文甲。"),
                    ExpectedProfile(12)),
                AudioCacheKey.FromSpeechTextHash(
                    "chapter-0",
                    StableSpeechSegmentIdentity.Body(10, 4),
                    Fingerprint.Sha256("正文乙。"),
                    ExpectedProfile(12))
            ],
            plan.OrderedSegmentKeys);
    }

    [Fact]
    public async Task ExportAsync_forwards_background_progress_to_writer_batch()
    {
        var metadata = CreateMetadata();
        var planStore = new FakeChapterSpeechPlanStore();
        planStore.Plans["chapter-0"] = CreatePlan(
            "chapter-0",
            [CreatePlanSegment(0, 0, "正文。")]);
        var writer = new FakeChapterMp3ExportWriter();
        var service = CreateService(
            metadata,
            planStore,
            writer,
            AppSettings.Default with { SelectedTtsRuleId = 7 });
        var progress = new CaptureProgress();

        await service.ExportAsync(
            new ExportChaptersRequest("book-1", [0], @"D:\exports"),
            progress,
            CancellationToken.None);

        Assert.Same(progress, writer.LastBatch?.Progress);
    }

    [Fact]
    public async Task ExportAsync_uses_frozen_names_for_directory_file_and_title_audio()
    {
        var metadata = CreateMetadata();
        var planStore = new FakeChapterSpeechPlanStore();
        planStore.Plans["chapter-0"] = CreatePlan(
            "chapter-0",
            [CreatePlanSegment(0, 0, "正文。")]);
        var writer = new FakeChapterMp3ExportWriter();
        var service = CreateService(
            metadata,
            planStore,
            writer,
            AppSettings.Default with
            {
                SelectedTtsRuleId = 7,
                ReadChapterTitle = true
            });

        var result = await service.ExportAsync(
            new ExportChaptersRequest(
                "book-1",
                [0],
                @"D:\exports",
                "提交时的书名",
                new Dictionary<int, string> { [0] = "提交时的章节名" }),
            CancellationToken.None);

        Assert.Equal(ExportChaptersStatus.Succeeded, result.Status);
        Assert.NotNull(writer.LastBatch);
        var batch = writer.LastBatch!;
        Assert.Equal("提交时的书名", batch.BookDirectoryName);
        var plan = Assert.Single(batch.Chapters);
        Assert.Equal("001_提交时的章节名", plan.FileNameBase);
        Assert.Equal(
            AudioCacheKey.FromSpeechTextHash(
                "chapter-0",
                StableSpeechSegmentIdentity.ChapterTitle(),
                Fingerprint.Sha256("提交时的章节名"),
                ExpectedProfile(10)),
            plan.OrderedSegmentKeys[0]);
    }

    [Fact]
    public async Task ExportAsync_inserts_title_key_without_changing_body_order_or_identity()
    {
        var metadata = CreateMetadata();
        var planStore = new FakeChapterSpeechPlanStore();
        planStore.Plans["chapter-0"] = CreatePlan(
            "chapter-0",
            [CreatePlanSegment(0, 0, "正文。")]);

        var withoutTitleWriter = new FakeChapterMp3ExportWriter();
        var withoutTitle = CreateService(
            metadata,
            planStore,
            withoutTitleWriter,
            AppSettings.Default with { SelectedTtsRuleId = 7, DefaultSpeakSpeed = 10 });
        await withoutTitle.ExportAsync(
            new ExportChaptersRequest("book-1", [0], @"D:\exports"),
            CancellationToken.None);

        var withTitleWriter = new FakeChapterMp3ExportWriter();
        var withTitle = CreateService(
            metadata,
            planStore,
            withTitleWriter,
            AppSettings.Default with
            {
                SelectedTtsRuleId = 7,
                DefaultSpeakSpeed = 10,
                ReadChapterTitle = true
            });
        await withTitle.ExportAsync(
            new ExportChaptersRequest("book-1", [0], @"D:\exports"),
            CancellationToken.None);

        var bodyKey = Assert.Single(Assert.Single(withoutTitleWriter.LastBatch!.Chapters).OrderedSegmentKeys);
        Assert.NotNull(withTitleWriter.LastBatch);
        var withTitleKeys = Assert.Single(withTitleWriter.LastBatch!.Chapters).OrderedSegmentKeys;
        Assert.Equal(2, withTitleKeys.Count);
        Assert.Equal(
            AudioCacheKey.FromSpeechTextHash(
                "chapter-0",
                StableSpeechSegmentIdentity.ChapterTitle(),
                Fingerprint.Sha256("第一章"),
                ExpectedProfile(10)),
            withTitleKeys[0]);
        Assert.Equal(bodyKey, withTitleKeys[1]);
    }

    [Fact]
    public async Task ExportAsync_returns_stable_status_when_plan_is_missing_or_not_ready()
    {
        var metadata = CreateMetadata();
        var missingPlanStore = new FakeChapterSpeechPlanStore();
        var missingWriter = new FakeChapterMp3ExportWriter();
        var missingResult = await CreateService(
                metadata,
                missingPlanStore,
                missingWriter,
                AppSettings.Default with { SelectedTtsRuleId = 7 })
            .ExportAsync(
                new ExportChaptersRequest("book-1", [0], @"D:\exports"),
                CancellationToken.None);

        Assert.Equal(ExportChaptersStatus.ChapterSpeechPlanUnavailable, missingResult.Status);
        Assert.Null(missingWriter.LastBatch);

        var notReadyPlanStore = new FakeChapterSpeechPlanStore
        {
            Plans =
            {
                ["chapter-0"] = CreatePlan(
                    "chapter-0",
                    [],
                    ChapterSpeechPlanState.Computing)
            }
        };
        var notReadyWriter = new FakeChapterMp3ExportWriter();
        var notReadyResult = await CreateService(
                metadata,
                notReadyPlanStore,
                notReadyWriter,
                AppSettings.Default with { SelectedTtsRuleId = 7 })
            .ExportAsync(
                new ExportChaptersRequest("book-1", [0], @"D:\exports"),
                CancellationToken.None);

        Assert.Equal(ExportChaptersStatus.ChapterSpeechPlanUnavailable, notReadyResult.Status);
        Assert.Null(notReadyWriter.LastBatch);
    }

    [Fact]
    public async Task ExportAsync_returns_no_playable_content_when_ready_plan_has_no_segments()
    {
        var writer = new FakeChapterMp3ExportWriter();
        var planStore = new FakeChapterSpeechPlanStore
        {
            Plans =
            {
                ["chapter-0"] = CreatePlan("chapter-0", [])
            }
        };

        var result = await CreateService(
                CreateMetadata(),
                planStore,
                writer,
                AppSettings.Default with { SelectedTtsRuleId = 7 })
            .ExportAsync(
                new ExportChaptersRequest("book-1", [0], @"D:\exports"),
                CancellationToken.None);

        Assert.Equal(ExportChaptersStatus.ChapterHasNoPlayableSegments, result.Status);
        Assert.Null(writer.LastBatch);
    }

    [Fact]
    public async Task ExportAsync_rejects_a_plan_when_current_text_profile_differs()
    {
        var currentRule = new RegexReplacementRule(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "当前规则",
            true,
            0,
            "原文",
            "语音",
            RegexReplacementScope.Speech,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch);
        var writer = new FakeChapterMp3ExportWriter();
        var planStore = new FakeChapterSpeechPlanStore
        {
            Plans =
            {
                ["chapter-0"] = CreatePlan("chapter-0", [CreatePlanSegment(0, 0, "正文。")])
            }
        };

        var result = await CreateService(
                CreateMetadata(),
                planStore,
                writer,
                AppSettings.Default with { SelectedTtsRuleId = 7 },
                [currentRule])
            .ExportAsync(
                new ExportChaptersRequest("book-1", [0], @"D:\exports"),
                CancellationToken.None);

        Assert.Equal(ExportChaptersStatus.ChapterSpeechPlanUnavailable, result.Status);
        Assert.Null(writer.LastBatch);
    }

    [Fact]
    public async Task ExportAsync_exports_title_when_ready_plan_has_no_body_segments()
    {
        var writer = new FakeChapterMp3ExportWriter();
        var planStore = new FakeChapterSpeechPlanStore
        {
            Plans =
            {
                ["chapter-0"] = CreatePlan("chapter-0", [])
            }
        };

        var result = await CreateService(
                CreateMetadata(),
                planStore,
                writer,
                AppSettings.Default with
                {
                    SelectedTtsRuleId = 7,
                    ReadChapterTitle = true
                })
            .ExportAsync(
                new ExportChaptersRequest("book-1", [0], @"D:\exports"),
                CancellationToken.None);

        Assert.Equal(ExportChaptersStatus.Succeeded, result.Status);
        Assert.NotNull(writer.LastBatch);
        var keys = Assert.Single(writer.LastBatch!.Chapters).OrderedSegmentKeys;
        Assert.Equal(
            AudioCacheKey.FromSpeechTextHash(
                "chapter-0",
                StableSpeechSegmentIdentity.ChapterTitle(),
                Fingerprint.Sha256("第一章"),
                ExpectedProfile(10)),
            Assert.Single(keys));
    }

    [Fact]
    public async Task ExportAsync_keeps_the_starting_synthesis_profile_when_settings_change_during_plan_reads()
    {
        var settings = new FakeAppSettingsService(AppSettings.Default with
        {
            SelectedTtsRuleId = 7,
            DefaultSpeakSpeed = 12
        });
        var planStore = new FakeChapterSpeechPlanStore
        {
            BeforeGet = () => settings.CurrentValue = settings.CurrentValue with
            {
                SelectedTtsRuleId = 8,
                DefaultSpeakSpeed = 5,
                ReadChapterTitle = true
            },
            Plans =
            {
                ["chapter-0"] = CreatePlan("chapter-0", [CreatePlanSegment(0, 0, "正文。")])
            }
        };
        var writer = new FakeChapterMp3ExportWriter();
        var service = new ExportChaptersService(
            CreateMetadata(),
            planStore,
            new FakeRegexReplacementRuleRepository([]),
            new FakeSelectedTtsRuleProvider(7),
            settings,
            new ExportFileNameSanitizer(),
            writer);

        await service.ExportAsync(
            new ExportChaptersRequest("book-1", [0], @"D:\exports"),
            CancellationToken.None);

        Assert.NotNull(writer.LastBatch);
        var key = Assert.Single(Assert.Single(writer.LastBatch!.Chapters).OrderedSegmentKeys);
        Assert.Equal(ExpectedProfile(12), key.Identity.SynthesisProfile);
    }

    [Fact]
    public async Task ExportAsync_maps_strict_writer_validation_failure_without_creating_output()
    {
        var writer = new FakeChapterMp3ExportWriter
        {
            Result = ChapterMp3ExportWriteResult.IncompleteCache(0)
        };
        var planStore = new FakeChapterSpeechPlanStore
        {
            Plans =
            {
                ["chapter-0"] = CreatePlan("chapter-0", [CreatePlanSegment(0, 0, "正文。")])
            }
        };

        var result = await CreateService(
                CreateMetadata(),
                planStore,
                writer,
                AppSettings.Default with { SelectedTtsRuleId = 7 })
            .ExportAsync(
                new ExportChaptersRequest("book-1", [0], @"D:\exports"),
                CancellationToken.None);

        Assert.Equal(ExportChaptersStatus.IncompleteCache, result.Status);
        Assert.Empty(result.Files);
        Assert.Equal(0, result.FailedChapterIndex);
    }

    [Fact]
    public async Task ExportAsync_propagates_cancellation_from_strict_writer_validation()
    {
        using var cancellation = new CancellationTokenSource();
        var writer = new FakeChapterMp3ExportWriter
        {
            BeforeWrite = cancellation.Cancel
        };
        var planStore = new FakeChapterSpeechPlanStore
        {
            Plans =
            {
                ["chapter-0"] = CreatePlan("chapter-0", [CreatePlanSegment(0, 0, "正文。")])
            }
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateService(
                    CreateMetadata(),
                    planStore,
                    writer,
                    AppSettings.Default with { SelectedTtsRuleId = 7 })
                .ExportAsync(
                    new ExportChaptersRequest("book-1", [0], @"D:\exports"),
                    cancellation.Token));
    }

    private static ExportChaptersService CreateService(
        FakeBookPlaybackMetadataQuery metadata,
        FakeChapterSpeechPlanStore planStore,
        FakeChapterMp3ExportWriter writer,
        AppSettings settings,
        IReadOnlyList<RegexReplacementRule>? regexRules = null)
    {
        return new ExportChaptersService(
            metadata,
            planStore,
            new FakeRegexReplacementRuleRepository(regexRules ?? []),
            new FakeSelectedTtsRuleProvider(settings.SelectedTtsRuleId),
            new FakeAppSettingsService(settings),
            new ExportFileNameSanitizer(),
            writer);
    }

    private static FakeBookPlaybackMetadataQuery CreateMetadata()
    {
        var query = new FakeBookPlaybackMetadataQuery
        {
            Book = new PlaybackBookMetadata(
                "book-1",
                "示例书",
                null,
                [new PlaybackChapterSummaryMetadata(0, "第一章")])
        };
        query.Chapters[0] = new PlaybackChapterMetadata(
            0,
            "第一章",
            "content.txt",
            0,
            20,
            "chapter-0");
        return query;
    }

    private static ChapterSpeechPlan CreatePlan(
        string chapterId,
        IReadOnlyList<ChapterSpeechPlanSegment> segments,
        ChapterSpeechPlanState state = ChapterSpeechPlanState.Ready) =>
        new(
            chapterId,
            Fingerprint.Sha256($"{chapterId}-revision"),
            TextProfileFingerprint.Create(TextSegmentationOptions.Default, []),
            Fingerprint.Sha256($"{chapterId}-plan-{segments.Count}"),
            state,
            segments.Count,
            DateTimeOffset.UnixEpoch,
            segments);

    private static ChapterSpeechPlanSegment CreatePlanSegment(
        int orderIndex,
        int sourceStartOffset,
        string speechText) =>
        new(
            orderIndex,
            SpeechSegmentKind.Body,
            sourceStartOffset,
            speechText.Length,
            Fingerprint.Sha256(speechText));

    private static SynthesisProfileFingerprint ExpectedProfile(int speakSpeed)
    {
        var rule = new NormalizedHttpTtsRule(
            7,
            "当前规则",
            NormalizedTemplate.Parse("https://cache-key.invalid/7"),
            new Dictionary<string, NormalizedTemplate>(),
            "GET",
            null,
            false,
            "audio/mpeg",
            null);
        return SynthesisProfileFingerprint.Create(
            TtsRuleFingerprint.Create(rule),
            speakSpeed);
    }

    private sealed class FakeBookPlaybackMetadataQuery : IBookPlaybackMetadataQuery
    {
        public PlaybackBookMetadata? Book { get; init; }

        public Dictionary<int, PlaybackChapterMetadata> Chapters { get; } = [];

        public Task<PlaybackBookMetadata?> GetBookAsync(
            string bookId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Book);
        }

        public Task<PlaybackChapterMetadata?> GetChapterAsync(
            string bookId,
            int chapterIndex,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Chapters.GetValueOrDefault(chapterIndex));
        }
    }

    private sealed class FakeChapterSpeechPlanStore : IChapterSpeechPlanStore
    {
        public Dictionary<string, ChapterSpeechPlan> Plans { get; init; } = [];

        public Action? BeforeGet { get; init; }

        public Task<ChapterSpeechPlan?> GetAsync(
            string chapterId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BeforeGet?.Invoke();
            return Task.FromResult(Plans.GetValueOrDefault(chapterId));
        }

        public Task SaveAsync(ChapterSpeechPlan plan, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeRegexReplacementRuleRepository(
        IReadOnlyList<RegexReplacementRule> rules) : IRegexReplacementRuleRepository
    {
        public Task<IReadOnlyList<RegexReplacementRule>> GetAllAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(rules);
        }

        public Task SaveAsync(RegexReplacementRule rule, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task UpdateEnabledAsync(Guid ruleId, bool isEnabled, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task SaveOrderAsync(
            IReadOnlyList<(Guid RuleId, int SortOrder)> order,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task DeleteAsync(Guid ruleId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeSelectedTtsRuleProvider(long? ruleId) : ISelectedTtsRuleProvider
    {
        public Task<SelectedPlaybackRule?> GetSelectedRuleAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(
                ruleId is null
                    ? null
                    : new SelectedPlaybackRule(
                        ruleId.Value,
                        "当前规则",
                        TestHttpTtsRules.Create(
                            ruleId.Value,
                            "当前规则",
                            "https://cache-key.invalid/7",
                            "audio/mpeg",
                            null,
                            null,
                            null,
                            null,
                            true,
                            null,
                            "2026-07-20T00:00:00.0000000Z",
                            "2026-07-20T00:00:00.0000000Z"),
                        new NormalizedHttpTtsRule(
                            ruleId.Value,
                            "当前规则",
                            NormalizedTemplate.Parse("https://cache-key.invalid/7"),
                            new Dictionary<string, NormalizedTemplate>(),
                            "GET",
                            null,
                            false,
                            "audio/mpeg",
                            null)));
        }

        public Task<SelectedPlaybackRule?> SelectRuleAsync(long selectedRuleId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeAppSettingsService(AppSettings settings) : IAppSettingsService
    {
        public AppSettings Current => CurrentValue;

        public AppSettings CurrentValue { get; set; } = settings;

        public event EventHandler<AppSettingsChangedEventArgs>? Changed
        {
            add { }
            remove { }
        }

        public Task<AppSettings> UpdateAsync(AppSettingsUpdate update, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class CaptureProgress : IProgress<ExportChaptersProgress>
    {
        public void Report(ExportChaptersProgress value)
        {
        }
    }

    private sealed class FakeChapterMp3ExportWriter : IChapterMp3ExportWriter
    {
        public ChapterMp3ExportBatch? LastBatch { get; private set; }

        public ChapterMp3ExportWriteResult Result { get; init; } =
            ChapterMp3ExportWriteResult.Succeeded(string.Empty, []);

        public Action? BeforeWrite { get; init; }

        public Task<ChapterMp3ExportWriteResult> WriteAsync(
            ChapterMp3ExportBatch batch,
            CancellationToken cancellationToken)
        {
            LastBatch = batch;
            BeforeWrite?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Result);
        }
    }
}
