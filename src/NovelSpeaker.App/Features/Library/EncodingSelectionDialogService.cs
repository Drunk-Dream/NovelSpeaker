using System.Windows;
using NovelSpeaker.Application.Books;
using NovelSpeaker.App.Shared.Dialogs;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace NovelSpeaker.App.Features.Library;

public sealed class EncodingSelectionDialogService : IEncodingSelectionDialogService
{
    private readonly IContentDialogService _contentDialogService;

    public EncodingSelectionDialogService(IContentDialogService contentDialogService)
    {
        _contentDialogService = contentDialogService;
    }

    public async Task<string?> ShowAsync(EncodingSelectionPrompt prompt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        if (_contentDialogService.GetDialogHostEx() is null)
        {
            return null;
        }

        var comboBox = new global::System.Windows.Controls.ComboBox
        {
            ItemsSource = prompt.AvailableEncodings,
            Margin = new Thickness(0, 16, 0, 0),
            MinWidth = 160
        };
        comboBox.SetResourceReference(FrameworkElement.StyleProperty, "App.Input.ComboBox.Standard");
        comboBox.SelectedItem = prompt.AvailableEncodings.Contains(prompt.DefaultEncoding, StringComparer.OrdinalIgnoreCase)
            ? prompt.AvailableEncodings.First(item => string.Equals(item, prompt.DefaultEncoding, StringComparison.OrdinalIgnoreCase))
            : prompt.AvailableEncodings.FirstOrDefault();

        var content = new global::System.Windows.Controls.StackPanel
        {
            Children =
            {
                AppDialogVisuals.CreateTitle(prompt.FileName),
                AppDialogVisuals.CreateMessage(prompt.Message),
                comboBox
            }
        };
        var dialog = AppDialogVisuals.Create(
            "选择文本编码",
            AppDialogVisuals.CreateBody(content),
            "继续导入",
            null,
            "取消");

        var result = await _contentDialogService.ShowAsync(dialog, cancellationToken);
        return result == ContentDialogResult.Primary
            ? comboBox.SelectedItem as string
            : null;
    }
}
