using NovelSpeaker.Application.Abstractions;
using NovelSpeaker.App.Shared.Presentation.Platform;
using NovelSpeaker.App.Shared.Presentation.Rules;
using Xunit;

namespace NovelSpeaker.App.PresentationTests.Shared;

public sealed class RuleDocumentInteractionTests
{
    [Fact]
    public async Task PickImportAsync_reads_selected_file_and_projects_safe_source_name()
    {
        var files = new FakeFileOperations();
        var service = new RuleDocumentInteraction(
            new FakeFileDialogs { OpenPath = "selected.json" },
            new FakeClipboard(),
            files);

        var document = await service.PickImportAsync(CancellationToken.None);

        Assert.Equal("{}", document!.Json);
        Assert.Equal("rules.json", document.SourceDescription);
        Assert.Equal("selected.json", files.ReadPath);
    }

    [Fact]
    public async Task Export_and_copy_use_shared_platform_ports()
    {
        var dialogs = new FakeFileDialogs { SavePath = "export.json" };
        var clipboard = new FakeClipboard();
        var files = new FakeFileOperations();
        var service = new RuleDocumentInteraction(dialogs, clipboard, files);

        var exported = await service.ExportAsync("rule.json", "{\"name\":\"demo\"}", CancellationToken.None);
        await service.CopyAsync("copy", CancellationToken.None);

        Assert.True(exported);
        Assert.Equal("rule.json", dialogs.LastSuggestedFileName);
        Assert.Equal("export.json", files.WritePath);
        Assert.Equal("{\"name\":\"demo\"}", files.WrittenText);
        Assert.Equal("copy", clipboard.Text);
    }

    private sealed class FakeFileDialogs : IPresentationFileDialogService
    {
        public string? OpenPath { get; init; }
        public string? SavePath { get; init; }
        public string? LastSuggestedFileName { get; private set; }
        public Task<string?> PickOpenFileAsync(PresentationFileDialogOptions options, CancellationToken cancellationToken) => Task.FromResult(OpenPath);
        public Task<string?> PickSaveFileAsync(PresentationFileDialogOptions options, CancellationToken cancellationToken)
        {
            LastSuggestedFileName = options.SuggestedFileName;
            return Task.FromResult(SavePath);
        }
        public Task<string?> PickFolderAsync(PresentationFolderDialogOptions options, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeClipboard : IPresentationClipboard
    {
        public string? Text { get; private set; }
        public Task<string?> GetTextAsync(CancellationToken cancellationToken) => Task.FromResult(Text);
        public Task SetTextAsync(string text, CancellationToken cancellationToken)
        {
            Text = text;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeFileOperations : IUserDocumentFileOperations
    {
        public string? ReadPath { get; private set; }
        public string? WritePath { get; private set; }
        public string? WrittenText { get; private set; }
        public Task<UserDocumentFileMetadata?> GetMetadataAsync(string filePath, CancellationToken cancellationToken) =>
            Task.FromResult<UserDocumentFileMetadata?>(new UserDocumentFileMetadata(filePath, "rules.json", ".json", 2));
        public Task<string> ReadTextAsync(string filePath, CancellationToken cancellationToken)
        {
            ReadPath = filePath;
            return Task.FromResult("{}");
        }
        public Task WriteTextAsync(string filePath, string content, CancellationToken cancellationToken)
        {
            WritePath = filePath;
            WrittenText = content;
            return Task.CompletedTask;
        }
    }
}
