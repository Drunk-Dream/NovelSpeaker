using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Wpf.Ui.Controls;
using WpfTextBlock = System.Windows.Controls.TextBlock;

namespace NovelSpeaker.App.Shared.Dialogs;

internal static class AppDialogVisuals
{
    public static ContentDialog Create(
        string title,
        object content,
        string primaryButtonText,
        string? secondaryButtonText,
        string closeButtonText,
        ControlAppearance primaryAppearance = ControlAppearance.Primary)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = content,
            PrimaryButtonText = primaryButtonText,
            CloseButtonText = closeButtonText,
            PrimaryButtonAppearance = primaryAppearance,
            SecondaryButtonAppearance = ControlAppearance.Secondary,
            CloseButtonAppearance = ControlAppearance.Secondary,
            DefaultButton = ContentDialogButton.Primary
        };

        if (!string.IsNullOrWhiteSpace(secondaryButtonText))
        {
            dialog.SecondaryButtonText = secondaryButtonText;
        }

        return dialog;
    }

    public static Border CreateBody(FrameworkElement content)
    {
        var body = new Border
        {
            Child = content
        };
        body.SetResourceReference(FrameworkElement.StyleProperty, "App.Feedback.DialogBody");
        return body;
    }

    public static WpfTextBlock CreateTitle(string text)
    {
        var title = CreateText(text, "App.Feedback.DialogTitle");
        title.SetValue(AutomationProperties.NameProperty, text);
        return title;
    }

    public static WpfTextBlock CreateMessage(string text)
    {
        var message = CreateText(text, "App.Feedback.DialogMessage");
        message.SetValue(AutomationProperties.NameProperty, text);
        return message;
    }

    private static WpfTextBlock CreateText(string text, string styleKey)
    {
        var textBlock = new WpfTextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap
        };
        textBlock.SetResourceReference(FrameworkElement.StyleProperty, styleKey);
        return textBlock;
    }
}
