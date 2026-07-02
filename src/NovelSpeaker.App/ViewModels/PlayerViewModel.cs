using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovelSpeaker.Application.Playback;
using NovelSpeaker.Application.Settings;
using NovelSpeaker.Application.Speech;
using NovelSpeaker.App.Feedback;
using NovelSpeaker.App.Navigation;
using NovelSpeaker.App.Pages;
using NovelSpeaker.App.Player;
using NovelSpeaker.Domain.Settings;
using System.Windows;
using Wpf.Ui;

namespace NovelSpeaker.App.ViewModels;

public sealed partial class PlayerViewModel : ObservableObject
{
    private readonly IPlaybackCoordinator _playbackCoordinator;
    private readonly IBookPlaybackContentService _bookPlaybackContentService;
    private readonly ITtsRuleLibraryService _ruleLibraryService;
    private readonly IAppSettingsStore _settingsStore;
    private readonly IAppFeedbackService _feedbackService;
    private readonly INavigationService _navigationService;
    private readonly IPlayerAutoScrollCoordinator _autoScrollCoordinator;

    private readonly Dictionary<int, PlaybackChapterContent> _chapterCache = [];
    private PlaybackBookContent? _loadedBook;
    private string? _requestedBookId;
    private int _loadedChapterIndex = -1;
    private int _defaultSpeakSpeed = AppSettings.DefaultSpeakSpeedValue;
    private int _bookLoadVersion;
    private int _chapterLoadVersion;

    public PlayerViewModel(
        IPlaybackCoordinator playbackCoordinator,
        IBookPlaybackContentService bookPlaybackContentService,
        ITtsRuleLibraryService ruleLibraryService,
        IAppSettingsStore settingsStore,
        IAppFeedbackService feedbackService,
        INavigationService navigationService,
        IPlayerAutoScrollCoordinator autoScrollCoordinator)
    {
        _playbackCoordinator = playbackCoordinator;
        _bookPlaybackContentService = bookPlaybackContentService;
        _ruleLibraryService = ruleLibraryService;
        _settingsStore = settingsStore;
        _feedbackService = feedbackService;
        _navigationService = navigationService;
        _autoScrollCoordinator = autoScrollCoordinator;

        ApplyAutoScrollState();
        ApplySnapshot(_playbackCoordinator.CurrentSnapshot);

        _playbackCoordinator.SnapshotChanged += OnSnapshotChanged;
        _autoScrollCoordinator.StateChanged += OnAutoScrollStateChanged;
    }

    public ObservableCollection<PlayerRuleItemViewModel> Rules { get; } = [];

    public ObservableCollection<PlayerChapterItemViewModel> Chapters { get; } = [];

    public ObservableCollection<PlayerSegmentItemViewModel> Segments { get; } = [];

    public bool HasRules => Rules.Count > 0;

    public bool ShowPlaybackControls => HasAvailableRule;

    public bool ShowNoRuleState => !HasAvailableRule;

    public bool ShowPlaybackErrorBar => IsFaulted && !string.IsNullOrWhiteSpace(ErrorText);

    public bool CanTogglePlayPause => HasAvailableRule && !IsFaulted;

    public bool CanDecreaseSpeakSpeed => SpeakSpeed > AppSettings.MinSpeakSpeed;

    public bool CanIncreaseSpeakSpeed => SpeakSpeed < AppSettings.MaxSpeakSpeed;

    public string SpeakSpeedButtonText => $"语速 {SpeakSpeed}";

    public string DisplayedSegmentCounterText => BuildSegmentCounterText(
        IsSegmentProgressDragging ? (int)Math.Round(SegmentProgressPreviewValue) : CurrentSegmentIndex,
        CurrentChapterSegmentCount);

    public bool ShouldAutoCenterCurrentSegment => _autoScrollCoordinator.ShouldAutoCenter;

    public bool ShowInlineLoadingState => CurrentPlaybackState is PlaybackState.Preparing or PlaybackState.Buffering or PlaybackState.Recovering;

    public string InlineLoadingText => CurrentPlaybackState switch
    {
        PlaybackState.Preparing => "正在准备",
        PlaybackState.Buffering => "正在加载",
        PlaybackState.Recovering => "正在恢复",
        _ => string.Empty
    };

    public double SegmentProgressMaximum => Math.Max(CurrentChapterSegmentCount - 1, 0);

    [ObservableProperty]
    private string currentTitle = "未打开书籍";

    [ObservableProperty]
    private string currentAuthor = "未知作者";

    [ObservableProperty]
    private string currentChapterTitle = "尚未定位章节";

    [ObservableProperty]
    private string errorText = string.Empty;

    [ObservableProperty]
    private string primaryActionText = "播放";

    [ObservableProperty]
    private bool isFaulted;

    [ObservableProperty]
    private bool hasAvailableRule = true;

    [ObservableProperty]
    private PlaybackState currentPlaybackState = PlaybackState.Idle;

    [ObservableProperty]
    private int speakSpeed = AppSettings.DefaultSpeakSpeedValue;

    [ObservableProperty]
    private int currentChapterIndex = -1;

    [ObservableProperty]
    private int currentSegmentIndex = -1;

    [ObservableProperty]
    private int currentChapterSegmentCount;

    [ObservableProperty]
    private bool canGoToPreviousChapter;

    [ObservableProperty]
    private bool canGoToNextChapter;

    [ObservableProperty]
    private bool canGoToPreviousSegment;

    [ObservableProperty]
    private bool canGoToNextSegment;

    [ObservableProperty]
    private bool isRuleMenuOpen;

    [ObservableProperty]
    private bool isSpeedMenuOpen;

    [ObservableProperty]
    private string speedEditorText = AppSettings.DefaultSpeakSpeedValue.ToString(CultureInfo.InvariantCulture);

    [ObservableProperty]
    private string speedEditorErrorText = string.Empty;

    [ObservableProperty]
    private PlayerChapterItemViewModel? currentChapterItem;

    [ObservableProperty]
    private PlayerSegmentItemViewModel? currentSegmentItem;

    [ObservableProperty]
    private bool showReturnToCurrentSegment;

    [ObservableProperty]
    private double segmentProgressValue;

    [ObservableProperty]
    private double segmentProgressPreviewValue;

    [ObservableProperty]
    private bool isSegmentProgressDragging;

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        var settings = await _settingsStore.LoadAsync(cancellationToken);
        _defaultSpeakSpeed = settings.DefaultSpeakSpeed;

        if (!AppSettings.IsValidSpeakSpeed(_playbackCoordinator.CurrentSnapshot.SpeakSpeed))
        {
            SpeakSpeed = _defaultSpeakSpeed;
        }

        await RefreshRulesAsync(cancellationToken);
        ApplySnapshot(_playbackCoordinator.CurrentSnapshot);
    }

    public async Task HandleNavigationAsync(PlayerNavigationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        _requestedBookId = request.BookId;
        CloseTransientPanels();

        var book = await EnsureBookLoadedAsync(request.BookId, cancellationToken);
        if (book is null)
        {
            HandleMissingBook();
            return;
        }

        var snapshot = _playbackCoordinator.CurrentSnapshot;
        if (request.Mode == PlayerNavigationMode.ReturnToCurrentSession ||
            string.Equals(snapshot.BookId, request.BookId, StringComparison.Ordinal))
        {
            ApplySnapshot(snapshot);
            await EnsureContentLoadedForSnapshotAsync(snapshot, cancellationToken);
            await RestoreMissingRuleSessionAsync(request.BookId, snapshot, cancellationToken);
            return;
        }

        await _playbackCoordinator.OpenPausedAsync(
            new OpenBookPlaybackRequest(
                request.BookId,
                null,
                null,
                ResolveSpeakSpeedForOpen()),
            cancellationToken);

        snapshot = _playbackCoordinator.CurrentSnapshot;
        ApplySnapshot(snapshot);
        await EnsureContentLoadedForSnapshotAsync(snapshot, cancellationToken);
    }

    public void OnPageNavigatedFrom()
    {
        CloseTransientPanels();
        _autoScrollCoordinator.ResetForPageLeave();
    }

    public void NotifyUserScrollInput()
    {
        _autoScrollCoordinator.NotifyUserScrollInput();
    }

    public void NotifyScrollbarDragStarted()
    {
        _autoScrollCoordinator.BeginScrollbarDrag();
    }

    public void NotifyScrollbarDragCompleted()
    {
        _autoScrollCoordinator.EndScrollbarDrag();
    }

    public void NotifyProgrammaticScrollStarted()
    {
        _autoScrollCoordinator.BeginProgrammaticScroll();
    }

    public void NotifyProgrammaticScrollCompleted()
    {
        _autoScrollCoordinator.EndProgrammaticScroll();
    }

    public void BeginSegmentProgressInteraction()
    {
        if (CurrentChapterSegmentCount <= 0)
        {
            return;
        }

        IsSegmentProgressDragging = true;
        SegmentProgressPreviewValue = SegmentProgressValue;
        OnPropertyChanged(nameof(DisplayedSegmentCounterText));
    }

    public void PreviewSegmentProgress(double value)
    {
        if (!IsSegmentProgressDragging)
        {
            return;
        }

        SegmentProgressPreviewValue = NormalizeSegmentProgressValue(value);
        OnPropertyChanged(nameof(DisplayedSegmentCounterText));
    }

    public async Task CommitSegmentProgressAsync(double value, CancellationToken cancellationToken)
    {
        if (CurrentChapterSegmentCount <= 0 || CurrentChapterIndex < 0)
        {
            CancelSegmentProgressInteraction();
            return;
        }

        var targetSegmentIndex = (int)Math.Round(NormalizeSegmentProgressValue(value));
        IsSegmentProgressDragging = false;
        SegmentProgressPreviewValue = targetSegmentIndex;
        OnPropertyChanged(nameof(DisplayedSegmentCounterText));

        if (targetSegmentIndex == CurrentSegmentIndex)
        {
            SegmentProgressValue = targetSegmentIndex;
            return;
        }

        await _playbackCoordinator.JumpToSegmentAsync(CurrentChapterIndex, targetSegmentIndex, cancellationToken);
    }

    public void CancelSegmentProgressInteraction()
    {
        if (!IsSegmentProgressDragging)
        {
            return;
        }

        IsSegmentProgressDragging = false;
        SegmentProgressPreviewValue = SegmentProgressValue;
        OnPropertyChanged(nameof(DisplayedSegmentCounterText));
    }

    [RelayCommand]
    private void Back()
    {
        if (!_navigationService.GoBack())
        {
            _navigationService.NavigateWithHierarchy(typeof(LibraryPage));
        }
    }

    [RelayCommand]
    private void ToggleRuleMenu()
    {
        IsSpeedMenuOpen = false;
        IsRuleMenuOpen = !IsRuleMenuOpen;
    }

    [RelayCommand]
    private void ToggleSpeedMenu()
    {
        IsRuleMenuOpen = false;
        if (!IsSpeedMenuOpen)
        {
            SpeedEditorText = SpeakSpeed.ToString(CultureInfo.InvariantCulture);
            SpeedEditorErrorText = string.Empty;
        }

        IsSpeedMenuOpen = !IsSpeedMenuOpen;
    }

    [RelayCommand]
    private void OpenRuleMenu()
    {
        IsSpeedMenuOpen = false;
        IsRuleMenuOpen = true;
    }

    [RelayCommand]
    private void OpenRulesManagement()
    {
        CloseTransientPanels();
        _navigationService.NavigateWithHierarchy(typeof(TtsRulesPage));
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task TogglePlayPauseAsync(CancellationToken cancellationToken)
    {
        if (!HasAvailableRule || CurrentPlaybackState == PlaybackState.Faulted)
        {
            return;
        }

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
        var bookId = snapshot.BookId ?? _requestedBookId ?? _loadedBook?.BookId;
        if (string.IsNullOrWhiteSpace(bookId))
        {
            return;
        }

        await _playbackCoordinator.StartAsync(
            new PlaybackStartRequest(
                bookId,
                snapshot.ChapterIndex >= 0 ? snapshot.ChapterIndex : CurrentChapterIndex,
                snapshot.SegmentIndex >= 0 ? snapshot.SegmentIndex : CurrentSegmentIndex,
                null,
                SpeakSpeed),
            cancellationToken);
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private Task PreviousSegmentAsync(CancellationToken cancellationToken)
    {
        return _playbackCoordinator.PreviousSegmentAsync(cancellationToken);
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private Task NextSegmentAsync(CancellationToken cancellationToken)
    {
        return _playbackCoordinator.NextSegmentAsync(cancellationToken);
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private Task PreviousChapterAsync(CancellationToken cancellationToken)
    {
        return _playbackCoordinator.PreviousChapterAsync(cancellationToken);
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private Task NextChapterAsync(CancellationToken cancellationToken)
    {
        return _playbackCoordinator.NextChapterAsync(cancellationToken);
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task SelectRuleAsync(PlayerRuleItemViewModel? rule, CancellationToken cancellationToken)
    {
        if (rule is null || !rule.IsEnabled || rule.IsSelected || _playbackCoordinator.CurrentSnapshot.RuleId == rule.Id)
        {
            return;
        }

        await _playbackCoordinator.ChangeRuleAsync(rule.Id, cancellationToken);
        await RefreshRulesAsync(cancellationToken);
        IsRuleMenuOpen = false;
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task ApplySpeakSpeedAsync(CancellationToken cancellationToken)
    {
        if (!TryParseSpeedEditorText(out var parsedSpeed))
        {
            return;
        }

        await ApplySpeakSpeedChangeAsync(parsedSpeed, cancellationToken);
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task IncreaseSpeakSpeedAsync(CancellationToken cancellationToken)
    {
        var currentSpeed = ResolvePendingSpeakSpeed();
        var nextSpeed = Math.Min(currentSpeed + 1, AppSettings.MaxSpeakSpeed);
        if (nextSpeed == currentSpeed)
        {
            return;
        }

        SpeedEditorText = nextSpeed.ToString(CultureInfo.InvariantCulture);
        SpeedEditorErrorText = string.Empty;
        await ApplySpeakSpeedChangeAsync(nextSpeed, cancellationToken);
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task DecreaseSpeakSpeedAsync(CancellationToken cancellationToken)
    {
        var currentSpeed = ResolvePendingSpeakSpeed();
        var nextSpeed = Math.Max(currentSpeed - 1, AppSettings.MinSpeakSpeed);
        if (nextSpeed == currentSpeed)
        {
            return;
        }

        SpeedEditorText = nextSpeed.ToString(CultureInfo.InvariantCulture);
        SpeedEditorErrorText = string.Empty;
        await ApplySpeakSpeedChangeAsync(nextSpeed, cancellationToken);
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task SelectChapterAsync(PlayerChapterItemViewModel? chapter, CancellationToken cancellationToken)
    {
        if (chapter is null)
        {
            return;
        }

        _autoScrollCoordinator.ResetForChapterChange();
        await _playbackCoordinator.JumpToChapterAsync(chapter.ChapterIndex, cancellationToken);
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private Task SelectSegmentAsync(PlayerSegmentItemViewModel? segment, CancellationToken cancellationToken)
    {
        if (segment is null || segment.SegmentIndex == CurrentSegmentIndex)
        {
            return Task.CompletedTask;
        }

        return _playbackCoordinator.JumpToSegmentAsync(segment.ChapterIndex, segment.SegmentIndex, cancellationToken);
    }

    [RelayCommand]
    private void ReturnToCurrentSegment()
    {
        _autoScrollCoordinator.ReturnToCurrentSegment();
    }

    [RelayCommand(AllowConcurrentExecutions = false)]
    private Task RetryCurrentSegmentAsync(CancellationToken cancellationToken)
    {
        return _playbackCoordinator.RetryCurrentSegmentAsync(cancellationToken);
    }

    partial void OnSpeakSpeedChanged(int value)
    {
        OnPropertyChanged(nameof(SpeakSpeedButtonText));
        OnPropertyChanged(nameof(CanDecreaseSpeakSpeed));
        OnPropertyChanged(nameof(CanIncreaseSpeakSpeed));
    }

    partial void OnCurrentSegmentIndexChanged(int value)
    {
        OnPropertyChanged(nameof(DisplayedSegmentCounterText));
        if (!IsSegmentProgressDragging)
        {
            SegmentProgressValue = NormalizeSegmentProgressValue(value);
            SegmentProgressPreviewValue = SegmentProgressValue;
        }
    }

    partial void OnCurrentChapterSegmentCountChanged(int value)
    {
        OnPropertyChanged(nameof(DisplayedSegmentCounterText));
        OnPropertyChanged(nameof(SegmentProgressMaximum));
        SegmentProgressValue = NormalizeSegmentProgressValue(CurrentSegmentIndex);
        if (!IsSegmentProgressDragging)
        {
            SegmentProgressPreviewValue = SegmentProgressValue;
        }
    }

    partial void OnIsSegmentProgressDraggingChanged(bool value)
    {
        OnPropertyChanged(nameof(DisplayedSegmentCounterText));
    }

    partial void OnIsFaultedChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowPlaybackErrorBar));
        OnPropertyChanged(nameof(CanTogglePlayPause));
    }

    partial void OnCurrentPlaybackStateChanged(PlaybackState value)
    {
        OnPropertyChanged(nameof(ShowInlineLoadingState));
        OnPropertyChanged(nameof(InlineLoadingText));
    }

    partial void OnHasAvailableRuleChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowPlaybackControls));
        OnPropertyChanged(nameof(ShowNoRuleState));
        OnPropertyChanged(nameof(CanTogglePlayPause));
    }

    partial void OnErrorTextChanged(string value)
    {
        OnPropertyChanged(nameof(ShowPlaybackErrorBar));
    }

    private void OnSnapshotChanged(object? sender, PlaybackSnapshot snapshot)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            _ = dispatcher.InvokeAsync(() => _ = HandleSnapshotUpdateAsync(snapshot));
            return;
        }

        _ = HandleSnapshotUpdateAsync(snapshot);
    }

    private void OnAutoScrollStateChanged(object? sender, EventArgs e)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            _ = dispatcher.InvokeAsync(ApplyAutoScrollState);
            return;
        }

        ApplyAutoScrollState();
    }

    private async Task HandleSnapshotUpdateAsync(PlaybackSnapshot snapshot)
    {
        ApplySnapshot(snapshot);

        if (string.IsNullOrWhiteSpace(snapshot.BookId))
        {
            return;
        }

        try
        {
            await EnsureContentLoadedForSnapshotAsync(snapshot, CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task RefreshRulesAsync(CancellationToken cancellationToken)
    {
        var rules = await _ruleLibraryService.GetRulesAsync(cancellationToken);
        Rules.ReplaceWith(rules, rule => new PlayerRuleItemViewModel(rule.Id, rule.Name, rule.IsEnabled, rule.IsSelected));
        ApplyRuleSelection(_playbackCoordinator.CurrentSnapshot.RuleId);
        OnPropertyChanged(nameof(HasRules));
    }

    private async Task EnsureContentLoadedForSnapshotAsync(PlaybackSnapshot snapshot, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(snapshot.BookId) || snapshot.ChapterIndex < 0)
        {
            return;
        }

        var book = await EnsureBookLoadedAsync(snapshot.BookId, cancellationToken);
        if (book is null)
        {
            return;
        }

        await EnsureChapterLoadedAsync(book.BookId, snapshot.ChapterIndex, cancellationToken);
        UpdateChapterProjection(snapshot.ChapterIndex);
        UpdateSegmentProjection(snapshot.SegmentIndex);
    }

    private async Task<PlaybackBookContent?> EnsureBookLoadedAsync(string bookId, CancellationToken cancellationToken)
    {
        if (_loadedBook is not null && string.Equals(_loadedBook.BookId, bookId, StringComparison.Ordinal))
        {
            return _loadedBook;
        }

        var loadVersion = ++_bookLoadVersion;
        var book = await _bookPlaybackContentService.GetBookAsync(bookId, cancellationToken);
        if (loadVersion != _bookLoadVersion)
        {
            return null;
        }

        if (book is null)
        {
            return null;
        }

        ApplyLoadedBook(book);
        return book;
    }

    private async Task EnsureChapterLoadedAsync(string bookId, int chapterIndex, CancellationToken cancellationToken)
    {
        if (_loadedBook is null || !string.Equals(_loadedBook.BookId, bookId, StringComparison.Ordinal))
        {
            return;
        }

        if (_chapterCache.TryGetValue(chapterIndex, out var cachedChapter))
        {
            ApplyChapterContent(cachedChapter);
            return;
        }

        var loadVersion = ++_chapterLoadVersion;
        var chapter = await _bookPlaybackContentService.GetChapterAsync(bookId, chapterIndex, cancellationToken);
        if (loadVersion != _chapterLoadVersion || chapter is null)
        {
            return;
        }

        _chapterCache[chapter.ChapterIndex] = chapter;
        ApplyChapterContent(chapter);
    }

    private void ApplyLoadedBook(PlaybackBookContent book)
    {
        var isDifferentBook = !string.Equals(_loadedBook?.BookId, book.BookId, StringComparison.Ordinal);
        _loadedBook = book;

        if (isDifferentBook)
        {
            _chapterCache.Clear();
            _loadedChapterIndex = -1;
            Segments.Clear();
        }

        Chapters.ReplaceWith(book.Chapters, chapter => new PlayerChapterItemViewModel(chapter.ChapterIndex, chapter.Title));

        if (string.IsNullOrWhiteSpace(_playbackCoordinator.CurrentSnapshot.BookTitle))
        {
            CurrentTitle = book.BookTitle;
        }

        if (string.IsNullOrWhiteSpace(_playbackCoordinator.CurrentSnapshot.BookAuthor))
        {
            CurrentAuthor = string.IsNullOrWhiteSpace(book.BookAuthor) ? "未知作者" : book.BookAuthor;
        }

        UpdateChapterProjection(CurrentChapterIndex);
        UpdateNavigationAvailability();
    }

    private void ApplyChapterContent(PlaybackChapterContent chapter)
    {
        if (_loadedChapterIndex != chapter.ChapterIndex)
        {
            _autoScrollCoordinator.ResetForChapterChange();
        }

        _loadedChapterIndex = chapter.ChapterIndex;
        CurrentChapterSegmentCount = chapter.Segments.Count;
        CurrentChapterTitle = chapter.Title;
        Segments.ReplaceWith(chapter.Segments, segment =>
            new PlayerSegmentItemViewModel(chapter.ChapterIndex, segment.SegmentIndex, segment.DisplayText));
        UpdateSegmentProjection(CurrentSegmentIndex);
        UpdateNavigationAvailability();
    }

    private void ApplySnapshot(PlaybackSnapshot snapshot)
    {
        var previousChapterIndex = CurrentChapterIndex;
        CurrentPlaybackState = snapshot.State;
        CurrentTitle = string.IsNullOrWhiteSpace(snapshot.BookTitle)
            ? _loadedBook?.BookTitle ?? "未打开书籍"
            : snapshot.BookTitle;
        CurrentAuthor = string.IsNullOrWhiteSpace(snapshot.BookAuthor)
            ? string.IsNullOrWhiteSpace(_loadedBook?.BookAuthor) ? "未知作者" : _loadedBook!.BookAuthor!
            : snapshot.BookAuthor;
        CurrentChapterTitle = string.IsNullOrWhiteSpace(snapshot.ChapterTitle)
            ? ResolveChapterTitle(snapshot.ChapterIndex)
            : snapshot.ChapterTitle;
        IsFaulted = snapshot.State == PlaybackState.Faulted;
        HasAvailableRule = snapshot.HasAvailableRule;
        ErrorText = IsFaulted ? snapshot.Message ?? "播放失败。" : string.Empty;
        PrimaryActionText = snapshot.State == PlaybackState.Playing ? "暂停" : "播放";

        var nextSpeakSpeed = AppSettings.NormalizeSpeakSpeed(
            snapshot.SpeakSpeed <= 0 ? _defaultSpeakSpeed : snapshot.SpeakSpeed);
        if (nextSpeakSpeed > 0)
        {
            SpeakSpeed = nextSpeakSpeed;
            if (!IsSpeedMenuOpen)
            {
                SpeedEditorText = nextSpeakSpeed.ToString(CultureInfo.InvariantCulture);
            }
        }

        CurrentChapterIndex = string.IsNullOrWhiteSpace(snapshot.BookId) ? -1 : snapshot.ChapterIndex;
        CurrentSegmentIndex = string.IsNullOrWhiteSpace(snapshot.BookId) ? -1 : snapshot.SegmentIndex;
        if (snapshot.SegmentCount > 0)
        {
            CurrentChapterSegmentCount = snapshot.SegmentCount;
        }

        if (previousChapterIndex != CurrentChapterIndex && previousChapterIndex >= 0)
        {
            _autoScrollCoordinator.ResetForChapterChange();
        }

        ApplyRuleSelection(snapshot.RuleId);
        UpdateChapterProjection(snapshot.ChapterIndex);
        UpdateSegmentProjection(snapshot.SegmentIndex);
        UpdateNavigationAvailability();
    }

    private void ApplyRuleSelection(long? selectedRuleId)
    {
        if (selectedRuleId is null)
        {
            return;
        }

        foreach (var rule in Rules)
        {
            rule.IsSelected = rule.Id == selectedRuleId.Value;
        }
    }

    private void UpdateChapterProjection(int currentChapterIndex)
    {
        PlayerChapterItemViewModel? currentItem = null;
        foreach (var chapter in Chapters)
        {
            var isCurrent = chapter.ChapterIndex == currentChapterIndex;
            chapter.IsCurrent = isCurrent;
            if (isCurrent)
            {
                currentItem = chapter;
            }
        }

        CurrentChapterItem = currentItem;
    }

    private void UpdateSegmentProjection(int currentSegmentIndex)
    {
        PlayerSegmentItemViewModel? currentItem = null;
        foreach (var segment in Segments)
        {
            if (segment is null)
            {
                continue;
            }

            var distance = Math.Abs(segment.SegmentIndex - currentSegmentIndex);
            segment.IsCurrent = segment.SegmentIndex == currentSegmentIndex;
            segment.FontWeight = distance == 0 ? FontWeights.SemiBold : FontWeights.Normal;
            segment.VisualOpacity = distance switch
            {
                0 => 1d,
                1 => 0.82d,
                2 => 0.68d,
                3 => 0.58d,
                _ => 0.46d
            };
            segment.IsInteractive = true;
            if (segment.IsCurrent)
            {
                currentItem = segment;
            }
        }

        CurrentSegmentItem = currentItem;
    }

    private void UpdateNavigationAvailability()
    {
        if (_loadedBook is null || CurrentChapterIndex < 0)
        {
            CanGoToPreviousChapter = false;
            CanGoToNextChapter = false;
        }
        else
        {
            var chapterPosition = GetChapterPosition(CurrentChapterIndex);
            CanGoToPreviousChapter = chapterPosition > 0;
            CanGoToNextChapter = chapterPosition >= 0 && chapterPosition < _loadedBook.Chapters.Count - 1;
        }

        CanGoToPreviousSegment = CurrentSegmentIndex > 0;
        CanGoToNextSegment = CurrentChapterSegmentCount > 0 && CurrentSegmentIndex >= 0 && CurrentSegmentIndex < CurrentChapterSegmentCount - 1;
    }

    private int GetChapterPosition(int chapterIndex)
    {
        if (_loadedBook is null)
        {
            return -1;
        }

        for (var i = 0; i < _loadedBook.Chapters.Count; i++)
        {
            if (_loadedBook.Chapters[i].ChapterIndex == chapterIndex)
            {
                return i;
            }
        }

        return -1;
    }

    private string ResolveChapterTitle(int chapterIndex)
    {
        if (_loadedBook is not null)
        {
            var chapter = _loadedBook.Chapters.FirstOrDefault(item => item.ChapterIndex == chapterIndex);
            if (chapter is not null)
            {
                return chapter.Title;
            }
        }

        return "尚未定位章节";
    }

    private void ApplyAutoScrollState()
    {
        ShowReturnToCurrentSegment = _autoScrollCoordinator.ShowReturnToCurrentSegment;
        OnPropertyChanged(nameof(ShouldAutoCenterCurrentSegment));
    }

    private void CloseTransientPanels()
    {
        IsRuleMenuOpen = false;
        IsSpeedMenuOpen = false;
        SpeedEditorErrorText = string.Empty;
    }

    private int ResolveSpeakSpeedForOpen()
    {
        return AppSettings.NormalizeSpeakSpeed(SpeakSpeed > 0 ? SpeakSpeed : _defaultSpeakSpeed);
    }

    private bool TryParseSpeedEditorText(out int parsedSpeed)
    {
        if (!int.TryParse(SpeedEditorText, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedSpeed) ||
            !AppSettings.IsValidSpeakSpeed(parsedSpeed))
        {
            SpeedEditorErrorText = $"请输入 {AppSettings.MinSpeakSpeed} 到 {AppSettings.MaxSpeakSpeed} 的整数。";
            return false;
        }

        SpeedEditorErrorText = string.Empty;
        return true;
    }

    private int ResolvePendingSpeakSpeed()
    {
        return int.TryParse(SpeedEditorText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedSpeed) &&
               AppSettings.IsValidSpeakSpeed(parsedSpeed)
            ? parsedSpeed
            : SpeakSpeed;
    }

    private async Task ApplySpeakSpeedChangeAsync(int parsedSpeed, CancellationToken cancellationToken)
    {
        if (parsedSpeed == SpeakSpeed &&
            AppSettings.NormalizeSpeakSpeed(_playbackCoordinator.CurrentSnapshot.SpeakSpeed) == parsedSpeed)
        {
            return;
        }

        await _playbackCoordinator.ChangeSpeedAsync(parsedSpeed, cancellationToken);
    }

    private async Task RestoreMissingRuleSessionAsync(
        string requestedBookId,
        PlaybackSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        if (snapshot.HasAvailableRule ||
            string.IsNullOrWhiteSpace(snapshot.BookId) ||
            !string.Equals(snapshot.BookId, requestedBookId, StringComparison.Ordinal))
        {
            return;
        }

        var selectedRule = Rules.FirstOrDefault(static rule => rule.IsEnabled && rule.IsSelected);
        if (selectedRule is null)
        {
            return;
        }

        await _playbackCoordinator.OpenPausedAsync(
            new OpenBookPlaybackRequest(
                requestedBookId,
                snapshot.ChapterIndex >= 0 ? snapshot.ChapterIndex : null,
                snapshot.SegmentIndex >= 0 ? snapshot.SegmentIndex : null,
                ResolveSpeakSpeedForOpen()),
            cancellationToken);

        var refreshedSnapshot = _playbackCoordinator.CurrentSnapshot;
        ApplySnapshot(refreshedSnapshot);
        await EnsureContentLoadedForSnapshotAsync(refreshedSnapshot, cancellationToken);
    }

    private void HandleMissingBook()
    {
        _feedbackService.ShowWarning("无法打开书籍", "这本书可能已经被删除。");
        _navigationService.NavigateWithHierarchy(typeof(LibraryPage));
    }

    private double NormalizeSegmentProgressValue(double value)
    {
        if (CurrentChapterSegmentCount <= 0)
        {
            return 0d;
        }

        return Math.Clamp(Math.Round(value), 0d, SegmentProgressMaximum);
    }

    private static string BuildSegmentCounterText(int segmentIndex, int segmentCount)
    {
        return segmentCount > 0 && segmentIndex >= 0
            ? $"第 {segmentIndex + 1} / {segmentCount} 段"
            : "尚未定位段落";
    }
}
