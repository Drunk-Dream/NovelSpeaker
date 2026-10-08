using NovelSpeaker.App.Shared.Dialogs;

namespace NovelSpeaker.App.Shared.Presentation.Workbenches;

/// <summary>
/// Owns the minimum state shared by feature-specific workbench editors.
/// </summary>
internal sealed class EditorSession<TId, TEditor>
    where TEditor : class
{
    private readonly Func<TEditor, TEditor, bool> _editorsEqual;
    private readonly bool _newIsDirty;

    public EditorSession(Func<TEditor, TEditor, bool> editorsEqual, bool newIsDirty = false)
    {
        _editorsEqual = editorsEqual;
        _newIsDirty = newIsDirty;
    }

    public bool HasEditor { get; private set; }

    public bool IsNew { get; private set; }

    public bool IsDirty { get; private set; }

    public TId EditorId { get; private set; } = default!;

    public TId FallbackId { get; private set; } = default!;

    public TEditor? Baseline { get; private set; }

    public bool IsEditing(TId editorId) =>
        HasEditor &&
        !IsNew &&
        EqualityComparer<TId>.Default.Equals(EditorId, editorId);

    public void Open(TId editorId, TEditor editor, bool isNew, TId fallbackId)
    {
        EditorId = editorId;
        FallbackId = fallbackId;
        Baseline = editor;
        HasEditor = true;
        IsNew = isNew;
        IsDirty = isNew && _newIsDirty;
    }

    public void Close()
    {
        EditorId = default!;
        FallbackId = default!;
        Baseline = null;
        HasEditor = false;
        IsNew = false;
        IsDirty = false;
    }

    public bool UpdateDirty(TEditor editor)
    {
        ArgumentNullException.ThrowIfNull(editor);
        IsDirty = HasEditor &&
                  Baseline is not null &&
                  (IsNew && _newIsDirty || !_editorsEqual(Baseline, editor));
        return IsDirty;
    }

    public async Task<bool> ConfirmLeaveAsync(
        Func<CancellationToken, Task<UnsavedChangesDecision>> decide,
        Func<CancellationToken, Task<bool>> save,
        Func<CancellationToken, Task> discard,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsDirty) return true;
        var decision = await decide(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (decision == UnsavedChangesDecision.Save)
        {
            var saved = await save(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return saved;
        }
        if (decision != UnsavedChangesDecision.Discard) return false;
        await discard(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return true;
    }
}
