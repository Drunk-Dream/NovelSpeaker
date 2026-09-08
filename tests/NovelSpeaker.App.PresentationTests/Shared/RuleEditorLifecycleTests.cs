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
    public void SelectionController_rejects_stale_selected_key_without_mutating_selection()
    {
        var selection = new RuleSelectionController<string>();
        selection.Select("rule-1");

        Assert.True(selection.IsSelected("rule-1"));
        Assert.False(selection.TryGetSelected(new HashSet<string>(["rule-2"]), out _));
        Assert.True(selection.HasSelection);

        selection.Clear();
        Assert.False(selection.HasSelection);
    }

    [Fact]
    public void ReorderController_supports_drop_and_offset_without_mutating_input()
    {
        var original = new[] { "one", "two", "three" };

        Assert.True(RuleReorderController.TryMove(
            original,
            "three",
            "one",
            RuleDropPlacement.Before,
            out var before,
            StringComparer.Ordinal));
        Assert.Equal(["three", "one", "two"], before);
        Assert.Equal(["one", "two", "three"], original);

        Assert.True(RuleReorderController.TryMoveByOffset(
            original,
            "one",
            1,
            out var offset,
            StringComparer.Ordinal));
        Assert.Equal(["two", "one", "three"], offset);
        Assert.False(RuleReorderController.TryMove(
            original,
            "one",
            "two",
            RuleDropPlacement.Before,
            out _,
            StringComparer.Ordinal));
        Assert.False(RuleReorderController.TryMove(
            original,
            "two",
            "one",
            RuleDropPlacement.After,
            out _,
            StringComparer.Ordinal));
        Assert.False(RuleReorderController.TryMoveByOffset(
            original,
            "missing",
            1,
            out _,
            StringComparer.Ordinal));
        Assert.False(RuleReorderController.TryMoveByOffset(
            original,
            "one",
            0,
            out _,
            StringComparer.Ordinal));
        Assert.False(RuleReorderController.TryMove(
            original,
            "one",
            "two",
            (RuleDropPlacement)999,
            out _,
            StringComparer.Ordinal));
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
            _ => Task.FromResult(true),
            () => isBusy,
            value => isBusy = value,
            CancellationToken.None);

        await entered.Task;
        var second = await session.RunAsync(
            _ => Task.FromResult<RuleImportDocument?>(new RuleImportDocument("{}", "test")),
            (_, _) => Task.FromResult(new RuleImportResult(1, 0, 1)),
            _ => Task.FromResult(true),
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

    [Fact]
    public async Task ImportSession_rejects_dirty_guard_busy_and_cancelled_operations()
    {
        var session = new RuleImportSession();
        var importCallCount = 0;
        var isBusy = false;
        var document = new RuleImportDocument("{}", "test");

        var rejected = await session.RunAsync(
            _ => Task.FromResult<RuleImportDocument?>(document),
            (_, _) =>
            {
                importCallCount++;
                return Task.FromResult(new RuleImportResult(1, 0, 1));
            },
            _ => Task.FromResult(false),
            () => isBusy,
            value => isBusy = value,
            CancellationToken.None);

        Assert.Null(rejected);
        Assert.Equal(0, importCallCount);
        Assert.False(isBusy);

        isBusy = true;
        var busy = await session.RunAsync(
            _ => Task.FromResult<RuleImportDocument?>(document),
            (_, _) =>
            {
                importCallCount++;
                return Task.FromResult(new RuleImportResult(1, 0, 1));
            },
            _ => Task.FromResult(true),
            () => isBusy,
            value => isBusy = value,
            CancellationToken.None);

        Assert.Null(busy);
        Assert.Equal(0, importCallCount);
        isBusy = false;

        using var cancellation = new CancellationTokenSource();
        var cancelled = session.RunAsync(
            _ =>
            {
                cancellation.Cancel();
                return Task.FromResult<RuleImportDocument?>(document);
            },
            (_, _) =>
            {
                importCallCount++;
                return Task.FromResult(new RuleImportResult(1, 0, 1));
            },
            _ => Task.FromResult(true),
            () => isBusy,
            value => isBusy = value,
            cancellation.Token);

        await Assert.ThrowsAsync<OperationCanceledException>(() => cancelled);
        Assert.Equal(0, importCallCount);
        Assert.False(isBusy);
    }

    [Fact]
    public async Task ImportSession_releases_busy_and_lock_when_cancelled_after_guard_or_import()
    {
        var session = new RuleImportSession();
        var isBusy = false;
        var importCallCount = 0;
        var document = new RuleImportDocument("{}", "test");

        using var guardCancellation = new CancellationTokenSource();
        var cancelledAfterGuard = session.RunAsync(
            _ => Task.FromResult<RuleImportDocument?>(document),
            (_, _) =>
            {
                importCallCount++;
                return Task.FromResult(new RuleImportResult(1, 0, 1));
            },
            _ =>
            {
                guardCancellation.Cancel();
                return Task.FromResult(true);
            },
            () => isBusy,
            value => isBusy = value,
            guardCancellation.Token);

        await Assert.ThrowsAsync<OperationCanceledException>(() => cancelledAfterGuard);
        Assert.False(isBusy);
        Assert.Equal(0, importCallCount);

        using var importCancellation = new CancellationTokenSource();
        await Assert.ThrowsAsync<OperationCanceledException>(() => session.RunAsync(
            _ => Task.FromResult<RuleImportDocument?>(document),
            (_, _) =>
            {
                importCallCount++;
                importCancellation.Cancel();
                return Task.FromResult(new RuleImportResult(1, 0, 1));
            },
            _ => Task.FromResult(true),
            () => isBusy,
            value => isBusy = value,
            importCancellation.Token));

        Assert.False(isBusy);
        Assert.Equal(1, importCallCount);

        var retry = await session.RunAsync(
            _ => Task.FromResult<RuleImportDocument?>(document),
            (_, _) =>
            {
                importCallCount++;
                return Task.FromResult(new RuleImportResult(1, 0, 1));
            },
            _ => Task.FromResult(true),
            () => isBusy,
            value => isBusy = value,
            CancellationToken.None);

        Assert.NotNull(retry);
        Assert.Equal(2, importCallCount);
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
            _ => Task.FromResult(true),
            () => isBusy,
            value => isBusy = value,
            CancellationToken.None));

        Assert.False(isBusy);
    }

    private sealed record Editor(string Value);

    private static bool EditorsEqual(Editor left, Editor right) => left.Value == right.Value;
}
