using NovelSpeaker.App.Shell.Activation;
using NovelSpeaker.App.Shell.Navigation;

namespace NovelSpeaker.App.Features.Rules.Metadata;

public sealed class TextHeaderMetadataRulesPage : MetadataRuleWorkbenchPage
{
    public TextHeaderMetadataRulesPage(TextHeaderMetadataRulesViewModel viewModel,
        INavigationGuardService guards, PageEventOperationRunner operations)
        : base(viewModel, guards, operations) { }
}
