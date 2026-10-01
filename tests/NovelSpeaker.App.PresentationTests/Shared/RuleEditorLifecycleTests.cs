using NovelSpeaker.App.Features.Rules.Shared;
using NovelSpeaker.App.Shared.Presentation.Rules;
using Xunit;

namespace NovelSpeaker.App.PresentationTests.Shared;

public sealed class RuleEditorLifecycleTests
{
    [Fact]
    public void EditorSession_tracks_open_dirty_new_and_fallback_state()
    {
        var session = new EditorSession<int, Editor>(EditorsEqual);
        var baseline = new Editor("before");

        session.Open(7, baseline, isNew: false, fallbackId: 3);

        Assert.True(session.HasEditor);
        Assert.False(session.IsNew);
        Assert.Equal(7, session.EditorId);
        Assert.Equal(3, session.FallbackId);
        Assert.True(session.IsEditing(7));
        Assert.False(session.IsEditing(3));
        Assert.False(session.UpdateDirty(new Editor("before")));
        Assert.True(session.UpdateDirty(new Editor("after")));

        session.Close();

        Assert.False(session.HasEditor);
        Assert.False(session.IsDirty);
        Assert.Null(session.Baseline);

        session.Open(default, new Editor("new"), isNew: true, fallbackId: 7);
        Assert.True(session.IsNew);
        Assert.Equal(7, session.FallbackId);
        Assert.False(session.IsEditing(7));
    }

    [Fact]
    public void Reorder_slots_support_first_last_and_adjacent_noop_without_mutating_input()
    {
        var order = new[] { "one", "two", "three" };
        Assert.True(RuleReorderController.TryMoveToSlot(order, order, "three", 0, out var first));
        Assert.Equal(["three", "one", "two"], first);
        Assert.True(RuleReorderController.TryMoveToSlot(order, order, "one", 3, out var last));
        Assert.Equal(["two", "three", "one"], last);
        Assert.False(RuleReorderController.TryMoveToSlot(order, order, "one", 1, out _));
        Assert.False(RuleReorderController.TryMoveToSlot(order, order, "two", 1, out _));
        Assert.False(RuleReorderController.TryMoveToSlot(order, order, "missing", 1, out _));
        Assert.False(RuleReorderController.TryMoveToSlot(order, order, "one", 4, out _));
        Assert.Equal(["one", "two", "three"], order);
        Assert.True(RuleReorderController.TryMoveByOffset(order, "one", 1, out var offset));
        Assert.Equal(["two", "one", "three"], offset);
    }

    [Theory]
    [InlineData("b", 0, "h0,b,a,h1,h2")]
    [InlineData("b", 1, "h0,a,h1,b,h2")]
    [InlineData("a", 1, "h0,h1,a,b,h2")]
    [InlineData("a", 2, "h0,h1,b,a,h2")]
    public void Visible_slots_map_to_complete_order(string source, int slot, string expected)
    {
        var complete = new[] { "h0", "a", "h1", "b", "h2" };
        var visible = new[] { "a", "b" };
        var changed = RuleReorderController.TryMoveToSlot(complete, visible, source, slot, out var result);
        Assert.Equal(expected != string.Join(',', complete), changed);
        Assert.Equal(expected.Split(','), changed ? result : complete);
    }

    [Fact]
    public async Task ImportSession_serializes_imports_and_releases_busy_after_completion()
    {
        var session = new RuleImportSession();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var isBusy = false;

        var first = session.RunAsync(
            _ => Task.FromResult<RuleImportDocument?>(new RuleImportDocument("{}", "test")),
            async (_, _) =>
            {
                entered.SetResult();
                await release.Task;
                return new RuleImportResult(1, 0, 1);
            },
            () => isBusy,
            value => isBusy = value,
            CancellationToken.None);

        await entered.Task;
        var second = await session.RunAsync(
            _ => Task.FromResult<RuleImportDocument?>(new RuleImportDocument("{}", "test")),
            (_, _) => Task.FromResult(new RuleImportResult(1, 0, 1)),
            () => isBusy,
            value => isBusy = value,
            CancellationToken.None);

        Assert.Null(second);
        Assert.True(isBusy);
        release.SetResult();
        var result = await first;

        Assert.NotNull(result);
        Assert.Equal("test", result!.Document.SourceDescription);
        Assert.False(isBusy);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ImportSession_cancellation_releases_busy_and_allows_retry(bool cancelDuringImport)
    {
        var session = new RuleImportSession();
        var isBusy = false;
        using var cancellation = new CancellationTokenSource();
        if (!cancelDuringImport) cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.RunAsync(
            _ => Task.FromResult<RuleImportDocument?>(new RuleImportDocument("{}", "test")),
            (_, _) =>
            {
                cancellation.Cancel();
                return Task.FromResult(new RuleImportResult(1, 0, 1));
            },
            () => isBusy, value => isBusy = value, cancellation.Token));
        Assert.False(isBusy);
        var retry = await session.RunAsync(
            _ => Task.FromResult<RuleImportDocument?>(new RuleImportDocument("{}", "test")),
            (_, _) => Task.FromResult(new RuleImportResult(1, 0, 1)),
            () => isBusy, value => isBusy = value, CancellationToken.None);
        Assert.NotNull(retry);
        Assert.False(isBusy);
    }

    [Fact]
    public async Task ImportSession_releases_busy_when_import_fails()
    {
        var session = new RuleImportSession();
        var isBusy = false;

        await Assert.ThrowsAsync<InvalidOperationException>(() => session.RunAsync<RuleImportResult>(
            _ => Task.FromResult<RuleImportDocument?>(new RuleImportDocument("{}", "test")),
            (_, _) => throw new InvalidOperationException("import failed"),
            () => isBusy,
            value => isBusy = value,
            CancellationToken.None));

        Assert.False(isBusy);
    }

    private sealed record Editor(string Value);

    private static bool EditorsEqual(Editor left, Editor right) => left.Value == right.Value;
}
