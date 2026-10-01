using NovelSpeaker.App.Shell.Activation;
using NovelSpeaker.App.Shell.Navigation;

namespace NovelSpeaker.App.Features.Rules.Metadata;

public sealed class FileNameMetadataRulesPage : MetadataRuleWorkbenchPage
{
    public FileNameMetadataRulesPage(FileNameMetadataRulesViewModel viewModel,
        INavigationGuardService guards, PageEventOperationRunner operations)
        : base(viewModel, guards, operations) { }
}
