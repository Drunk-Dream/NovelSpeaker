using System.Windows;
using NovelSpeaker.Application.Books;
using NovelSpeaker.App.Shared.Dialogs;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace NovelSpeaker.App.Features.Books.Library;

internal sealed class BookImportSelectionDialogService(IContentDialogService contentDialogService) : IBookImportSelectionDialogService
{
    public async Task<BookImportSelection?> ShowAsync(
        IReadOnlyList<BookImportCandidate> candidates, CancellationToken cancellationToken)
    {
        if (contentDialogService.GetDialogHostEx() is null)
        {
            return null;
        }

        var choices = new global::System.Windows.Controls.ComboBox { MinWidth = 320, MaxWidth = 560 };
        choices.SetResourceReference(FrameworkElement.StyleProperty, "App.Input.ComboBox.Standard");
        foreach (var candidate in candidates)
        {
            var label = $"{candidate.Title} · {(string.IsNullOrEmpty(candidate.Author) ? "无作者" : candidate.Author)}\n" +
                $"导入：{candidate.ImportedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss} · " +
                $"最近阅读：{(candidate.LastPlayedAt is { } played ? played.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") : "未阅读")}\n" +
                $"原文件：{candidate.OriginalFileName ?? "无本地来源"}";
            choices.Items.Add(new global::System.Windows.Controls.ComboBoxItem
            {
                Content = AppDialogVisuals.CreateMessage(label),
                Tag = candidate.BookId
            });
        }

        var content = new global::System.Windows.Controls.StackPanel
        {
            Children =
            {
                AppDialogVisuals.CreateMessage("找到多本同名同作者的书籍。请选择要更新的书籍，或作为新书导入。"),
                choices
            }
        };
        var dialog = AppDialogVisuals.Create("选择导入目标", AppDialogVisuals.CreateBody(content),
            "更新所选书籍", "作为新书导入", "取消");
        dialog.IsPrimaryButtonEnabled = false;
        choices.SelectionChanged += (_, _) => dialog.IsPrimaryButtonEnabled = choices.SelectedItem is not null;
        var result = await contentDialogService.ShowAsync(dialog, cancellationToken);
        return result switch
        {
            ContentDialogResult.Primary when choices.SelectedItem is global::System.Windows.Controls.ComboBoxItem item =>
                new BookImportSelection((string)item.Tag, false),
            ContentDialogResult.Secondary => new BookImportSelection(null, true),
            _ => null
        };
    }
}
