using NovelSpeaker.App.Navigation;

namespace NovelSpeaker.App.Pages;

public partial class BookDetailsPage : System.Windows.Controls.Page, IAppNavigationPage, System.ComponentModel.INotifyPropertyChanged
{
    private readonly IAppNavigationService _navigationService;
    private string _placeholderText = "当前版本仅建立书籍详情页导航壳，详细内容将在后续任务实现。";

    public BookDetailsPage(IAppNavigationService navigationService)
    {
        _navigationService = navigationService;
        InitializeComponent();
        DataContext = this;
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    public string PlaceholderText
    {
        get => _placeholderText;
        private set
        {
            if (string.Equals(_placeholderText, value, StringComparison.Ordinal))
            {
                return;
            }

            _placeholderText = value;
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(PlaceholderText)));
        }
    }

    public BookDetailsNavigationRequest? LastRequest { get; private set; }

    public Task OnNavigatedToAsync(AppNavigationEntry entry, CancellationToken cancellationToken)
    {
        LastRequest = entry.Parameter as BookDetailsNavigationRequest;
        PlaceholderText = LastRequest is null
            ? "当前版本仅建立书籍详情页导航壳，详细内容将在后续任务实现。"
            : $"已接收书籍详情导航参数，BookId: {LastRequest.BookId}";
        return Task.CompletedTask;
    }

    private void BackButton_OnClick(object sender, System.Windows.RoutedEventArgs e)
    {
        _navigationService.GoBack();
    }
}
