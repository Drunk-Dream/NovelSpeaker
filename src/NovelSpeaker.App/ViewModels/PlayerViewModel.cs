using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovelSpeaker.Application.Books;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Application.Speech;
using NovelSpeaker.App.Navigation;

namespace NovelSpeaker.App.ViewModels;

public sealed partial class PlayerViewModel : ObservableObject
{
    private readonly IPlaybackCoordinator _playbackCoordinator;
    private readonly IBookCatalogService _bookCatalogService;
    private readonly ITtsRuleLibraryService _ruleLibraryService;
    private readonly IAppSettingsStore _settingsStore;

    public PlayerViewModel(
        IPlaybackCoordinator playbackCoordinator,
        IBookCatalogService bookCatalogService,
        ITtsRuleLibraryService ruleLibraryService,
        IAppSettingsStore settingsStore)
    {
        _playbackCoordinator = playbackCoordinator;
        _bookCatalogService = bookCatalogService;
        _ruleLibraryService = ruleLibraryService;
        _settingsStore = settingsStore;
        ApplySnapshot(_playbackCoordinator.CurrentSnapshot);
        _playbackCoordinator.SnapshotChanged += OnSnapshotChanged;
    }

    public ObservableCollection<LibraryBookItemViewModel> Books { get; } = [];

    public ObservableCollection<PlayerRuleItemViewModel> Rules { get; } = [];

    [ObservableProperty]
    private string headline = "选择书籍、规则与语速，然后开始真实播放。";

    [ObservableProperty]
    private string currentTitle = "未开始播放";

    [ObservableProperty]
    private string currentChapterTitle = "尚未定位章节";

    [ObservableProperty]
    private string currentRuleText = "当前规则：未选择规则";

    [ObservableProperty]
    private string statusText = "请选择一本书并开始播放。";

    [ObservableProperty]
    private string detailText = "播放页会展示当前章节、段落位置、规则与错误恢复入口。";

    [ObservableProperty]
    private string errorText = string.Empty;

    [ObservableProperty]
    private string primaryActionText = "播放";

    [ObservableProperty]
    private bool isFaulted;

    [ObservableProperty]
    private bool canRetryCurrentSegment;

    [ObservableProperty]
    private bool canSkipCurrentSegment;

    [ObservableProperty]
    private PlaybackState currentPlaybackState = PlaybackState.Idle;

    [ObservableProperty]
    private long positionMilliseconds;

    [ObservableProperty]
    private long durationMilliseconds;

    [ObservableProperty]
    private LibraryBookItemViewModel? selectedBook;

    [ObservableProperty]
    private PlayerRuleItemViewModel? selectedRule;

    [ObservableProperty]
    private int speakSpeed = 10;

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        var settings = await _settingsStore.LoadAsync(cancellationToken);
        await RefreshBooksAsync(cancellationToken);

        var rules = await _ruleLibraryService.GetRulesAsync(cancellationToken);
        Rules.ReplaceWith(rules, rule => new PlayerRuleItemViewModel(rule.Id, rule.Name, rule.IsEnabled, rule.IsSelected));

        var snapshot = _playbackCoordinator.CurrentSnapshot;
        SelectedBook = Books.SelectByKeyOrFallback(
            snapshot.BookId,
            book => book.Id,
            SelectedBook);

        SelectedRule = Rules.SelectByKeyOrFallback(
            snapshot.RuleId,
            rule => rule.Id,
            SelectedRule,
            rule => rule.IsSelected);

        if (string.IsNullOrWhiteSpace(snapshot.BookId))
        {
            SpeakSpeed = settings.DefaultSpeakSpeed;
        }
    }

    public async Task HandleNavigationAsync(PlayerNavigationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!Books.Any(book => string.Equals(book.Id, request.BookId, StringComparison.Ordinal)))
        {
            await RefreshBooksAsync(cancellationToken);
        }

        SelectedBook = Books.SelectByKeyOrFallback(
            request.BookId,
            book => book.Id,
            SelectedBook);

        var snapshot = _playbackCoordinator.CurrentSnapshot;
        if (request.Mode == PlayerNavigationMode.ReturnToCurrentSession ||
            string.Equals(snapshot.BookId, request.BookId, StringComparison.Ordinal))
        {
            ApplySnapshot(snapshot);
            return;
        }

        await _playbackCoordinator.OpenPausedAsync(
            new OpenBookPlaybackRequest(request.BookId, null, null, SpeakSpeed),
            cancellationToken);

        ApplySnapshot(_playbackCoordinator.CurrentSnapshot);
    }

    [RelayCommand]
    private async Task StartSelectedBookAsync(CancellationToken cancellationToken)
    {
        if (SelectedBook is null)
        {
            StatusText = "请先选择一本书。";
            return;
        }

        await _playbackCoordinator.StartAsync(
            new PlaybackStartRequest(
                SelectedBook.Id,
                null,
                null,
                null,
                SpeakSpeed),
            cancellationToken);
    }

    [RelayCommand]
    private async Task TogglePlayPauseAsync(CancellationToken cancellationToken)
    {
        if (CurrentPlaybackState == PlaybackState.Playing)
        {
            await _playbackCoordinator.PauseAsync(cancellationToken);
            return;
        }

        if (CurrentPlaybackState == PlaybackState.Paused)
        {
            await _playbackCoordinator.ResumeAsync(cancellationToken);
            return;
        }

        var snapshot = _playbackCoordinator.CurrentSnapshot;
        if (!string.IsNullOrWhiteSpace(snapshot.BookId))
        {
            await _playbackCoordinator.StartAsync(
                new PlaybackStartRequest(
                    snapshot.BookId!,
                    snapshot.ChapterIndex,
                    snapshot.SegmentIndex,
                    null,
                    SpeakSpeed),
                cancellationToken);
            return;
        }

        await StartSelectedBookAsync(cancellationToken);
    }

    [RelayCommand]
    private Task StopAsync(CancellationToken cancellationToken)
    {
        return _playbackCoordinator.StopAsync(cancellationToken);
    }

    [RelayCommand]
    private Task PreviousSegmentAsync(CancellationToken cancellationToken)
    {
        return _playbackCoordinator.PreviousSegmentAsync(cancellationToken);
    }

    [RelayCommand]
    private Task NextSegmentAsync(CancellationToken cancellationToken)
    {
        return _playbackCoordinator.NextSegmentAsync(cancellationToken);
    }

    [RelayCommand]
    private Task PreviousChapterAsync(CancellationToken cancellationToken)
    {
        return _playbackCoordinator.PreviousChapterAsync(cancellationToken);
    }

    [RelayCommand]
    private Task NextChapterAsync(CancellationToken cancellationToken)
    {
        return _playbackCoordinator.NextChapterAsync(cancellationToken);
    }

    [RelayCommand]
    private Task RetryCurrentSegmentAsync(CancellationToken cancellationToken)
    {
        return _playbackCoordinator.RetryCurrentSegmentAsync(cancellationToken);
    }

    [RelayCommand]
    private Task SkipCurrentSegmentAsync(CancellationToken cancellationToken)
    {
        return _playbackCoordinator.SkipCurrentSegmentAsync(cancellationToken);
    }

    [RelayCommand]
    private async Task ApplySelectedRuleAsync(CancellationToken cancellationToken)
    {
        if (SelectedRule is null)
        {
            StatusText = "请先选择一条规则。";
            return;
        }

        await _playbackCoordinator.ChangeRuleAsync(SelectedRule.Id, cancellationToken);
        await LoadAsync(cancellationToken);
    }

    [RelayCommand]
    private Task ApplySpeakSpeedAsync(CancellationToken cancellationToken)
    {
        return _playbackCoordinator.ChangeSpeedAsync(SpeakSpeed, cancellationToken);
    }

    private void OnSnapshotChanged(object? sender, PlaybackSnapshot snapshot)
    {
        ApplySnapshot(snapshot);
    }

    private async Task RefreshBooksAsync(CancellationToken cancellationToken)
    {
        var books = await _bookCatalogService.GetBooksAsync(cancellationToken);
        Books.ReplaceWith(books, book => new LibraryBookItemViewModel(
            book.Id,
            book.Title,
            book.Author,
            book.CurrentChapterTitle,
            book.ImportedAt,
            book.LastPlayedAt));
    }

    private void ApplySnapshot(PlaybackSnapshot snapshot)
    {
        CurrentPlaybackState = snapshot.State;
        CurrentTitle = string.IsNullOrWhiteSpace(snapshot.BookTitle) ? "未开始播放" : snapshot.BookTitle;
        CurrentChapterTitle = string.IsNullOrWhiteSpace(snapshot.ChapterTitle)
            ? "尚未定位章节"
            : $"{snapshot.ChapterTitle}";
        CurrentRuleText = string.IsNullOrWhiteSpace(snapshot.RuleName)
            ? "当前规则：未选择规则"
            : $"当前规则：{snapshot.RuleName}";
        PositionMilliseconds = snapshot.PositionMilliseconds;
        DurationMilliseconds = snapshot.DurationMilliseconds;
        IsFaulted = snapshot.State == PlaybackState.Faulted;
        CanRetryCurrentSegment = snapshot.CanRetry;
        CanSkipCurrentSegment = snapshot.CanSkip;
        ErrorText = IsFaulted ? snapshot.Message ?? "播放失败。" : string.Empty;
        StatusText = BuildStatusText(snapshot);
        DetailText = BuildDetailText(snapshot);
        PrimaryActionText = snapshot.State == PlaybackState.Playing ? "暂停" : "播放";
        SpeakSpeed = snapshot.SpeakSpeed <= 0 ? SpeakSpeed : snapshot.SpeakSpeed;

        if (!string.IsNullOrWhiteSpace(snapshot.BookId))
        {
            SelectedBook = Books.SelectByKeyOrFallback(snapshot.BookId, book => book.Id, SelectedBook);
        }

        if (snapshot.RuleId is not null)
        {
            SelectedRule = Rules.SelectByKeyOrFallback(
                snapshot.RuleId,
                rule => rule.Id,
                SelectedRule,
                rule => rule.IsSelected);
        }
    }

    private static string BuildStatusText(PlaybackSnapshot snapshot)
    {
        return snapshot.State switch
        {
            PlaybackState.Preparing => "正在准备播放会话",
            PlaybackState.Buffering => "正在加载当前段音频",
            PlaybackState.Playing => "正在播放",
            PlaybackState.Paused => "已暂停",
            PlaybackState.Stopped => "已停止",
            PlaybackState.Recovering => "正在恢复当前段",
            PlaybackState.Faulted => "播放失败",
            _ => "待机中"
        };
    }

    private static string BuildDetailText(PlaybackSnapshot snapshot)
    {
        if (!string.IsNullOrWhiteSpace(snapshot.Message))
        {
            return snapshot.Message;
        }

        if (snapshot.DurationMilliseconds <= 0)
        {
            return "选择一本书并开始播放后，这里会显示段落位置、规则和错误恢复信息。";
        }

        var cacheText = snapshot.IsUsingCache ? "缓存命中" : "在线生成";
        return $"第 {snapshot.ChapterIndex + 1} 章，第 {snapshot.SegmentIndex + 1}/{Math.Max(snapshot.SegmentCount, 1)} 段 · {cacheText} · {snapshot.PositionMilliseconds} / {snapshot.DurationMilliseconds} ms";
    }
}
