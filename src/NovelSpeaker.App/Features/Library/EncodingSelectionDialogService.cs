using System.Windows;
using NovelSpeaker.Application.Books;
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
            Margin = new Thickness(0, 12, 0, 0),
            MinWidth = 160
        };
        comboBox.SelectedItem = prompt.AvailableEncodings.Contains(prompt.DefaultEncoding, StringComparer.OrdinalIgnoreCase)
            ? prompt.AvailableEncodings.First(item => string.Equals(item, prompt.DefaultEncoding, StringComparison.OrdinalIgnoreCase))
            : prompt.AvailableEncodings.FirstOrDefault();

        var dialog = new ContentDialog
        {
            Title = "选择文本编码",
            Content = new global::System.Windows.Controls.StackPanel
            {
                Children =
                {
                    new global::System.Windows.Controls.TextBlock
                    {
                        Text = prompt.FileName,
                        FontWeight = FontWeights.SemiBold
                    },
                    new global::System.Windows.Controls.TextBlock
                    {
                        Margin = new Thickness(0, 8, 0, 0),
                        Text = prompt.Message,
                        TextWrapping = TextWrapping.Wrap
                    },
                    comboBox
                }
            },
            PrimaryButtonText = "继续导入",
            CloseButtonText = "取消"
        };

        var result = await _contentDialogService.ShowAsync(dialog, cancellationToken);
        return result == ContentDialogResult.Primary
            ? comboBox.SelectedItem as string
            : null;
    }
}
