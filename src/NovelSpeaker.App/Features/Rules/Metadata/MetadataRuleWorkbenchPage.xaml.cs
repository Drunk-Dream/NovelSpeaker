using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using NovelSpeaker.App.Shared.Presentation.Selection;
using NovelSpeaker.App.Shell.Activation;
using NovelSpeaker.App.Shell.Navigation;
using Wpf.Ui.Abstractions.Controls;

namespace NovelSpeaker.App.Features.Rules.Metadata;

public partial class MetadataRuleWorkbenchPage : System.Windows.Controls.Page,
    INavigationAware, INavigableView<MetadataRuleWorkbenchViewModel>
{
    private readonly PageActivationController _activation = new();
    private readonly INavigationGuardService _guards;
    private readonly PageEventOperationRunner _operations;
    public ICommand SelectionCommand { get; }

    protected MetadataRuleWorkbenchPage(
        MetadataRuleWorkbenchViewModel viewModel,
        INavigationGuardService guards,
        PageEventOperationRunner operations)
        : this()
    {
        ViewModel = viewModel;
        _guards = guards;
        _operations = operations;
        SelectionCommand = new AsyncRelayCommand<MetadataRuleRow>(SelectWithModifiersAsync);
        DataContext = viewModel;
    }

    internal MetadataRuleWorkbenchPage()
    {
        ViewModel = null!;
        _guards = null!;
        _operations = PageEventOperationRunner.DesignTime;
        SelectionCommand = new AsyncRelayCommand<MetadataRuleRow>(SelectWithModifiersAsync);
        InitializeComponent();
    }

    public MetadataRuleWorkbenchViewModel ViewModel { get; }

    private Task SelectWithModifiersAsync(MetadataRuleRow? row)
    {
        var modifiers = DesktopSelectionModifiers.None;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) modifiers |= DesktopSelectionModifiers.Control;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) modifiers |= DesktopSelectionModifiers.Shift;
        return ViewModel.SelectRuleWithModifiersAsync(row, modifiers,
            _activation.Current?.CancellationToken ?? CancellationToken.None);
    }

    public async Task OnNavigatedToAsync()
    {
        using var operation = _operations.StartCriticalLoad(NovelSpeaker.Application.Observability.OperationCatalog.UiRulesLoad);
        var activation = _activation.Activate();
        activation.Register(_guards.Register(ViewModel.ConfirmLeaveAsync));
        try
        {
            await ViewModel.LoadAsync(activation.CancellationToken);
            operation.Complete(NovelSpeaker.Application.Observability.OperationResult.Succeeded());
        }
        catch (OperationCanceledException) when (!activation.IsCurrent)
        {
            operation.Complete(NovelSpeaker.Application.Observability.OperationResult.Cancelled());
        }
        catch
        {
            operation.Complete(NovelSpeaker.Application.Observability.OperationResult.Failed("page-load-failed"));
            throw;
        }
    }

    public Task OnNavigatedFromAsync()
    {
        _activation.Deactivate();
        return Task.CompletedTask;
    }
}
