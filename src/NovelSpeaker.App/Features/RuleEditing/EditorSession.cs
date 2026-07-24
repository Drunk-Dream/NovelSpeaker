namespace NovelSpeaker.App.Features.RuleEditing;

/// <summary>
/// Owns the minimum state shared by feature-specific rule editors.
/// </summary>
internal sealed class EditorSession<TId, TEditor>
    where TEditor : class
{
    private readonly Func<TEditor, TEditor, bool> _editorsEqual;

    public EditorSession(Func<TEditor, TEditor, bool> editorsEqual)
    {
        _editorsEqual = editorsEqual;
    }

    public bool HasEditor { get; private set; }

    public bool IsNew { get; private set; }

    public bool IsDirty { get; private set; }

    public TId EditorId { get; private set; } = default!;

    public TId FallbackId { get; private set; } = default!;

    public TEditor? Baseline { get; private set; }

    public void Open(TId editorId, TEditor editor, bool isNew, TId fallbackId)
    {
        EditorId = editorId;
        FallbackId = fallbackId;
        Baseline = editor;
        HasEditor = true;
        IsNew = isNew;
        IsDirty = false;
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
        IsDirty = HasEditor &&
                  Baseline is not null &&
                  !_editorsEqual(Baseline, editor);
        return IsDirty;
    }
}
