using CommunityToolkit.Mvvm.ComponentModel;

namespace NovelSpeaker.App.Shell;

/// <summary>
/// Mutable presentation row keyed by ChapterIndex for one frozen active-cache batch.
/// </summary>
public sealed class ShellActiveCacheChapterItem : ObservableObject
{
    private string _title;
    private string _statusText;
    private bool _isCurrent;
    private bool _isCompleted;
    private bool _isFailed;

    internal ShellActiveCacheChapterItem(
        int chapterIndex,
        string title,
        string statusText,
        bool isCurrent,
        bool isCompleted,
        bool isFailed)
    {
        ChapterIndex = chapterIndex;
        _title = title;
        _statusText = statusText;
        _isCurrent = isCurrent;
        _isCompleted = isCompleted;
        _isFailed = isFailed;
    }

    public int ChapterIndex { get; }

    public string Title => _title;

    public string StatusText => _statusText;

    public bool IsCurrent => _isCurrent;

    public bool IsCompleted => _isCompleted;

    public bool IsFailed => _isFailed;

    internal void Update(
        string title,
        string statusText,
        bool isCurrent,
        bool isCompleted,
        bool isFailed)
    {
        SetProperty(ref _title, title, nameof(Title));
        SetProperty(ref _statusText, statusText, nameof(StatusText));
        SetProperty(ref _isCurrent, isCurrent, nameof(IsCurrent));
        SetProperty(ref _isCompleted, isCompleted, nameof(IsCompleted));
        SetProperty(ref _isFailed, isFailed, nameof(IsFailed));
    }
}
