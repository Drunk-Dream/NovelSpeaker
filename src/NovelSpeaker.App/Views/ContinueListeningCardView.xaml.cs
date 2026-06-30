using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NovelSpeaker.App.ViewModels;

namespace NovelSpeaker.App.Views;

public partial class ContinueListeningCardView : UserControl
{
    public static readonly DependencyProperty ItemProperty =
        DependencyProperty.Register(
            nameof(Item),
            typeof(ContinueListeningItemViewModel),
            typeof(ContinueListeningCardView),
            new PropertyMetadata(null));

    public static readonly DependencyProperty OpenCommandProperty =
        DependencyProperty.Register(
            nameof(OpenCommand),
            typeof(ICommand),
            typeof(ContinueListeningCardView),
            new PropertyMetadata(null));

    public ContinueListeningCardView()
    {
        InitializeComponent();
    }

    public ContinueListeningItemViewModel? Item
    {
        get => (ContinueListeningItemViewModel?)GetValue(ItemProperty);
        set => SetValue(ItemProperty, value);
    }

    public ICommand? OpenCommand
    {
        get => (ICommand?)GetValue(OpenCommandProperty);
        set => SetValue(OpenCommandProperty, value);
    }
}
