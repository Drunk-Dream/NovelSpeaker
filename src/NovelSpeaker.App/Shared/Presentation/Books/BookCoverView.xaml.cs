using System.Windows;
using System.Windows.Controls;
using NovelSpeaker.App.Features.Library;

namespace NovelSpeaker.App.Shared.Presentation.Books;

public partial class BookCoverView : UserControl
{
    public static readonly DependencyProperty CoverProperty =
        DependencyProperty.Register(
            nameof(Cover),
            typeof(GeneratedBookCover),
            typeof(BookCoverView),
            new PropertyMetadata(null));

    public BookCoverView()
    {
        InitializeComponent();
    }

    public GeneratedBookCover? Cover
    {
        get => (GeneratedBookCover?)GetValue(CoverProperty);
        set => SetValue(CoverProperty, value);
    }
}
