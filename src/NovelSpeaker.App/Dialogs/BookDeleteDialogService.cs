using System.Windows;
using System.Windows.Controls;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace NovelSpeaker.App.Dialogs;

public sealed class BookDeleteDialogService : IBookDeleteDialogService
{
    private readonly IContentDialogService _contentDialogService;

    public BookDeleteDialogService(IContentDialogService contentDialogService)
    {
        _contentDialogService = contentDialogService;
    }

    public async Task<BookDeleteDialogResult> ShowAsync(BookDeleteDialogRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_contentDialogService.GetDialogHostEx() is null)
        {
            var fallback = global::System.Windows.MessageBox.Show(
                BuildFallbackMessage(request),
                "删除书籍",
                global::System.Windows.MessageBoxButton.OKCancel,
                global::System.Windows.MessageBoxImage.Warning);
            return new BookDeleteDialogResult(
                fallback == global::System.Windows.MessageBoxResult.OK,
                request.DeleteAudioCacheByDefault);
        }

        var deleteCacheCheckBox = new global::System.Windows.Controls.CheckBox
        {
            Content = "同时删除音频缓存",
            IsChecked = request.DeleteAudioCacheByDefault,
            Margin = new Thickness(0, 12, 0, 0)
        };
        var content = new global::System.Windows.Controls.StackPanel
        {
            Children =
            {
                new global::System.Windows.Controls.TextBlock
                {
                    Text = $"将删除《{request.BookTitle}》的书籍记录、章节、阅读进度和应用内部 TXT 副本。",
                    TextWrapping = TextWrapping.Wrap
                },
                new global::System.Windows.Controls.TextBlock
                {
                    Margin = new Thickness(0, 8, 0, 0),
                    Text = "不会删除用户最初选择的外部 TXT 文件。",
                    TextWrapping = TextWrapping.Wrap
                },
                new global::System.Windows.Controls.TextBlock
                {
                    Margin = new Thickness(0, 8, 0, 0),
                    Text = request.IsCurrentPlaybackBook
                        ? "这本书当前正在播放，确认后会先停止播放并结束当前会话。"
                        : "此操作不可撤销。",
                    TextWrapping = TextWrapping.Wrap
                },
                deleteCacheCheckBox
            }
        };
        var dialog = new ContentDialog
        {
            Title = "删除书籍",
            Content = content,
            PrimaryButtonText = "删除",
            CloseButtonText = "取消"
        };

        var result = await _contentDialogService.ShowAsync(dialog, cancellationToken).ConfigureAwait(false);
        return new BookDeleteDialogResult(
            result == ContentDialogResult.Primary,
            deleteCacheCheckBox.IsChecked != false);
    }

    private static string BuildFallbackMessage(BookDeleteDialogRequest request)
    {
        var playbackLine = request.IsCurrentPlaybackBook
            ? "删除后会先停止当前播放。"
            : "此操作不可撤销。";
        return $"将删除《{request.BookTitle}》的书籍记录、章节、阅读进度和应用内部 TXT 副本。\n不会删除用户最初选择的外部 TXT 文件。\n{playbackLine}";
    }
}
