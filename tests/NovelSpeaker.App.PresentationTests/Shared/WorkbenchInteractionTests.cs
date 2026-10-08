using NovelSpeaker.App.Shared.Presentation.Rules;
using NovelSpeaker.App.Shared.Presentation.Selection;
using Xunit;

namespace NovelSpeaker.App.PresentationTests.Shared;

public sealed class WorkbenchInteractionTests
{
    [Fact]
    public async Task Management_entry_cannot_overlap_or_commit_a_cancelled_guard_and_can_retry()
    {
        var selection = new ManagementSelectionController<int>();
        selection.SetItems([1, 2]);
        using var cancellation = new CancellationTokenSource();
        var decision = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entry = selection.TryEnterAsync(() => false, _ => decision.Task, cancellation.Token);
        Assert.False(await selection.TryEnterAsync(() => false, _ => Task.FromResult(true), CancellationToken.None));
        cancellation.Cancel();
        selection.Reset();
        decision.SetResult(true);
        Assert.False(await entry);
        Assert.False(selection.IsManagementMode);
        Assert.Empty(selection.SelectedItems);
        Assert.True(await selection.TryEnterAsync(() => false, _ => Task.FromResult(true), CancellationToken.None));
    }

    [Fact]
    public async Task Batch_delete_serializes_confirmation_and_continues_skips_and_failures_then_reconciles()
    {
        var session = new BatchDeleteSession();
        var confirmation = new TaskCompletionSource<IReadOnlyList<int>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var deleted = new List<int>();
        var busy = false;
        var refreshed = false;
        var batch = session.RunAsync(_ => confirmation.Task, (int item, CancellationToken _) =>
        {
            Assert.True(busy);
            if (item == 1) return Task.FromResult(false);
            if (item == 2) throw new InvalidOperationException("fixture delete failure");
            deleted.Add(item);
            return Task.FromResult(true);
        }, _ =>
        {
            Assert.True(busy);
            refreshed = true;
            return Task.CompletedTask;
        }, value => busy = value, CancellationToken.None);
        Assert.Null(await session.RunAsync<int>(_ => throw new InvalidOperationException("overlapping confirmation"),
            (_, _) => Task.FromResult(true), _ => Task.CompletedTask, value => busy = value, CancellationToken.None));
        Assert.False(busy);
        confirmation.SetResult([1, 2, 3]);
        var result = await batch;
        Assert.Equal(new BatchDeleteResult(1, 1, 1), result);
        Assert.Equal([3], deleted);
        Assert.True(refreshed);
        Assert.False(busy);
    }

    [Fact]
    public async Task Cancelled_batch_does_not_continue_or_complete_and_releases_busy_for_retry()
    {
        var session = new BatchDeleteSession();
        using var cancellation = new CancellationTokenSource();
        var busy = false;
        var deleted = new List<int>();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.RunAsync<int>(
            _ => Task.FromResult<IReadOnlyList<int>>([1, 2]),
            (item, _) =>
            {
                deleted.Add(item);
                cancellation.Cancel();
                return Task.FromResult(true);
            }, _ => throw new InvalidOperationException("cancelled refresh"), value => busy = value, cancellation.Token));
        Assert.Equal([1], deleted);
        Assert.False(busy);
        var retry = await session.RunAsync<int>(_ => Task.FromResult<IReadOnlyList<int>>([2]),
            (_, _) => Task.FromResult(true), _ => Task.CompletedTask, value => busy = value, CancellationToken.None);
        Assert.Equal(new BatchDeleteResult(1, 0, 0), retry);
        Assert.False(busy);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Exchange_does_not_write_a_document_that_arrives_after_cancellation(bool clipboard)
    {
        var documents = new FakeRuleDocumentInteraction();
        var exchange = new WorkbenchExchangeInteraction(documents);
        using var cancellation = new CancellationTokenSource();
        var document = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var write = exchange.WriteAsync(_ => document.Task, clipboard, "fixture.json", cancellation.Token);
        cancellation.Cancel();
        document.SetResult("{}");
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => write);
        Assert.Null(documents.ExportedJson);
        Assert.Null(documents.CopiedJson);
    }
}
