using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Books.TextProcessing;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Playback.Cache;
using NovelSpeaker.Application.Playback.Export;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Domain.Books;
using NovelSpeaker.Domain.Settings;
using Xunit;

namespace NovelSpeaker.Application.UnitTests;

public sealed class ExportChaptersServiceTests
{
    [Fact]
    public async Task ExportAsync_freezes_current_configuration_and_builds_ordered_chapter_plans()
    {
        var metadata = CreateMetadata();
        var reader = new FakeBookContentReader
        {
            TextByStartOffset =
            {
                [0] = "原文甲。",
                [10] = "原文乙。"
            }
        };
        var rules = new FakeRegexReplacementRuleRepository
        {
            Rules =
            [
                new RegexReplacementRule(
                    Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    "语音替换",
                    true,
                    0,
                    "原文",
                    "当前文本",
                    RegexReplacementScope.Speech,
                    DateTimeOffset.UnixEpoch,
                    DateTimeOffset.UnixEpoch)
            ]
        };
        var writer = new FakeChapterMp3ExportWriter
        {
            Result = ChapterMp3ExportWriteResult.Succeeded(
                @"D:\exports\_CON",
                [
                    new ExportedChapterMp3(0, @"D:\exports\_CON\001_第一章.mp3"),
                    new ExportedChapterMp3(1, @"D:\exports\_CON\002_第二章.mp3")
                ])
        };
        var settings = AppSettings.Default with
        {
            DefaultSpeakSpeed = 12,
            SelectedTtsRuleId = 7
        };
        var service = CreateService(metadata, reader, rules, writer, settings);

        var result = await service.ExportAsync(
            new ExportChaptersRequest("book-1", [1, 0, 1], @"D:\exports"),
            CancellationToken.None);

        Assert.Equal(ExportChaptersStatus.Succeeded, result.Status);
        Assert.Equal(2, result.Files.Count);
        var batch = Assert.IsType<ChapterMp3ExportBatch>(writer.LastBatch);
        Assert.Equal(@"D:\exports", batch.DestinationRootDirectory);
        Assert.Equal("_CON", batch.BookDirectoryName);
        Assert.Equal([0, 1], batch.Chapters.Select(chapter => chapter.ChapterIndex));
        Assert.Equal(["001_第一章", "002_第二章"], batch.Chapters.Select(chapter => chapter.FileNameBase));
        Assert.Equal(
            AudioCacheKey.FromPlayback("book-1", 0, 0, 7, 12, "当前文本甲。"),
            Assert.Single(batch.Chapters[0].OrderedSegmentKeys));
        Assert.Equal(
            AudioCacheKey.FromPlayback("book-1", 1, 0, 7, 12, "当前文本乙。"),
            Assert.Single(batch.Chapters[1].OrderedSegmentKeys));
        Assert.Equal(1, rules.GetAllCallCount);
    }

    [Fact]
    public async Task ExportAsync_includes_chapter_title_when_reading_titles_is_enabled()
    {
        var writer = new FakeChapterMp3ExportWriter();
        var settings = AppSettings.Default with
        {
            DefaultSpeakSpeed = 12,
            SelectedTtsRuleId = 7,
            ReadChapterTitle = true
        };
        var service = CreateService(
            CreateMetadata(),
            new FakeBookContentReader
            {
                TextByStartOffset =
                {
                    [0] = "正文。"
                }
            },
            new FakeRegexReplacementRuleRepository(),
            writer,
            settings);

        var result = await service.ExportAsync(
            new ExportChaptersRequest("book-1", [0], @"D:\exports"),
            CancellationToken.None);

        Assert.Equal(ExportChaptersStatus.Succeeded, result.Status);
        Assert.Equal(
            [
                AudioCacheKey.FromPlayback("book-1", 0, 0, 7, 12, "第一章"),
                AudioCacheKey.FromPlayback("book-1", 0, 1, 7, 12, "正文。")
            ],
            Assert.Single(writer.LastBatch!.Chapters).OrderedSegmentKeys);
    }

    [Fact]
    public async Task ExportAsync_rejects_incomplete_cache_without_generating_audio()
    {
        var writer = new FakeChapterMp3ExportWriter
        {
            Result = ChapterMp3ExportWriteResult.IncompleteCache(1)
        };
        var service = CreateService(
            CreateMetadata(),
            new FakeBookContentReader
            {
                TextByStartOffset =
                {
                    [0] = "第一章。",
                    [10] = "第二章。"
                }
            },
            new FakeRegexReplacementRuleRepository(),
            writer,
            AppSettings.Default with { SelectedTtsRuleId = 7 });

        var result = await service.ExportAsync(
            new ExportChaptersRequest("book-1", [0, 1], @"D:\exports"),
            CancellationToken.None);

        Assert.Equal(ExportChaptersStatus.IncompleteCache, result.Status);
        Assert.Equal(1, result.FailedChapterIndex);
        Assert.Empty(result.Files);
    }

    [Fact]
    public async Task ExportAsync_propagates_cancellation_from_the_writer()
    {
        using var cancellation = new CancellationTokenSource();
        var writer = new FakeChapterMp3ExportWriter
        {
            BeforeWrite = cancellation.Cancel
        };
        var service = CreateService(
            CreateMetadata(),
            new FakeBookContentReader
            {
                TextByStartOffset =
                {
                    [0] = "第一章。"
                }
            },
            new FakeRegexReplacementRuleRepository(),
            writer,
            AppSettings.Default with { SelectedTtsRuleId = 7 });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.ExportAsync(
                new ExportChaptersRequest("book-1", [0], @"D:\exports"),
                cancellation.Token));
    }

    [Fact]
    public async Task ExportAsync_keeps_the_starting_settings_snapshot_when_settings_change_mid_operation()
    {
        var metadata = CreateMetadata();
        var settings = new FakeAppSettingsService(AppSettings.Default with
        {
            DefaultSpeakSpeed = 12,
            SelectedTtsRuleId = 7
        });
        var reader = new FakeBookContentReader
        {
            TextByStartOffset =
            {
                [0] = "第一章。"
            },
            BeforeRead = () => settings.CurrentValue = settings.CurrentValue with
            {
                DefaultSpeakSpeed = 5,
                SelectedTtsRuleId = 8
            }
        };
        var writer = new FakeChapterMp3ExportWriter();
        var service = new ExportChaptersService(
            metadata,
            reader,
            new TextSegmenter(),
            new FakeRegexReplacementRuleRepository(),
            new FakeSelectedTtsRuleProvider(7),
            settings,
            new ExportFileNameSanitizer(),
            writer);

        await service.ExportAsync(
            new ExportChaptersRequest("book-1", [0], @"D:\exports"),
            CancellationToken.None);

        Assert.Equal(
            AudioCacheKey.FromPlayback("book-1", 0, 0, 7, 12, "第一章。"),
            Assert.Single(Assert.Single(writer.LastBatch!.Chapters).OrderedSegmentKeys));
    }

    private static ExportChaptersService CreateService(
        FakeBookPlaybackMetadataQuery metadata,
        FakeBookContentReader reader,
        FakeRegexReplacementRuleRepository rules,
        FakeChapterMp3ExportWriter writer,
        AppSettings settings)
    {
        return new ExportChaptersService(
            metadata,
            reader,
            new TextSegmenter(),
            rules,
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
                "CON",
                null,
                [
                    new PlaybackChapterSummaryMetadata(0, "第一章"),
                    new PlaybackChapterSummaryMetadata(1, "第二章")
                ])
        };
        query.Chapters[0] = new PlaybackChapterMetadata(0, "第一章", "content.txt", 0, 5);
        query.Chapters[1] = new PlaybackChapterMetadata(1, "第二章", "content.txt", 10, 5);
        return query;
    }

    private sealed class FakeBookPlaybackMetadataQuery : IBookPlaybackMetadataQuery
    {
        public PlaybackBookMetadata? Book { get; init; }

        public Dictionary<int, PlaybackChapterMetadata> Chapters { get; } = [];

        public Task<PlaybackBookMetadata?> GetBookAsync(string bookId, CancellationToken cancellationToken)
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

    private sealed class FakeBookContentReader : IBookContentReader
    {
        public Dictionary<int, string> TextByStartOffset { get; } = [];

        public Action? BeforeRead { get; init; }

        public Task<string> ReadChapterTextAsync(
            string storedFilePath,
            int startOffset,
            int length,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BeforeRead?.Invoke();
            return Task.FromResult(TextByStartOffset[startOffset]);
        }
    }

    private sealed class FakeRegexReplacementRuleRepository : IRegexReplacementRuleRepository
    {
        public IReadOnlyList<RegexReplacementRule> Rules { get; init; } = [];

        public int GetAllCallCount { get; private set; }

        public Task<IReadOnlyList<RegexReplacementRule>> GetAllAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GetAllCallCount++;
            return Task.FromResult(Rules);
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
                    : new SelectedPlaybackRule(ruleId.Value, "当前规则", null!, null!));
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
