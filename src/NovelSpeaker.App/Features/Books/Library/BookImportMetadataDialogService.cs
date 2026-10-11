using System.Windows;
using System.Windows.Automation;
using NovelSpeaker.Application.Books;
using NovelSpeaker.App.Shared.Dialogs;
using Wpf.Ui;
using Wpf.Ui.Controls;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace NovelSpeaker.App.Features.Books.Library;

internal sealed class BookImportMetadataDialogService(IContentDialogService contentDialogService) : IBookImportMetadataDialogService
{
    public async Task<BookImportIdentity?> ShowAsync(BookImportIdentity defaults, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (contentDialogService.GetDialogHostEx() is null)
        {
            return null;
        }

        var title = CreateInput(defaults.Title, "书名");
        var author = CreateInput(defaults.Author ?? "", "作者（可留空）");
        var content = new global::System.Windows.Controls.StackPanel
        {
            Children =
            {
                AppDialogVisuals.CreateMessage("书名"), title,
                AppDialogVisuals.CreateMessage("作者（可留空）"), author
            }
        };
        var dialog = AppDialogVisuals.Create("确认书籍信息", AppDialogVisuals.CreateBody(content), "导入", null, "取消");
        dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(title.Text);
        title.TextChanged += (_, _) => dialog.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(title.Text);
        var result = await contentDialogService.ShowAsync(dialog, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(title.Text)
            ? new BookImportIdentity(title.Text.Trim(), author.Text.Trim())
            : null;
    }

    private static WpfTextBox CreateInput(string text, string label)
    {
        var input = new WpfTextBox { Text = text, MinWidth = 320, MaxWidth = 560, Margin = new Thickness(0, 4, 0, 12) };
        input.SetResourceReference(FrameworkElement.StyleProperty, "App.Input.TextBox.Standard");
        AutomationProperties.SetName(input, label);
        return input;
    }
}
