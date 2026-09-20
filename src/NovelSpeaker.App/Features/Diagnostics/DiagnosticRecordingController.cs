using NovelSpeaker.Application.Diagnostics;

namespace NovelSpeaker.App.Features.Diagnostics;

/// <summary>
/// Routes recording lifecycle operations and projects the session owner's snapshot for the UI.
/// The session service remains the only mutable session-state owner.
/// </summary>
internal sealed class DiagnosticRecordingController
{
    private readonly IDiagnosticSessionService _sessions;
    private DiagnosticSessionSnapshot? _recoveredSnapshot;

    public DiagnosticRecordingController(IDiagnosticSessionService sessions)
    {
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
    }

    public DiagnosticSessionSnapshot? Snapshot =>
        _recoveredSnapshot = _sessions.Current ?? _sessions.LastEnded ?? _recoveredSnapshot;

    public async Task<DiagnosticSessionSnapshot?> RecoverAsync(CancellationToken cancellationToken)
    {
        _recoveredSnapshot = await _sessions.RecoverAsync(cancellationToken).ConfigureAwait(false)
            ?? _sessions.Current
            ?? _sessions.LastEnded;
        return Snapshot;
    }

    public async Task<DiagnosticSessionSnapshot> StartAsync(
        long hardCapBytes,
        CancellationToken cancellationToken)
    {
        var snapshot = await _sessions.StartAsync(
            new DiagnosticSessionStartOptions(hardCapBytes),
            cancellationToken).ConfigureAwait(false);
        _recoveredSnapshot = snapshot;
        return Snapshot ?? snapshot;
    }

    public async Task<DiagnosticSessionSnapshot> EndAsync(CancellationToken cancellationToken)
    {
        var snapshot = await _sessions.EndAsync(cancellationToken).ConfigureAwait(false);
        _recoveredSnapshot = snapshot;
        return Snapshot ?? snapshot;
    }

    public async Task AddAttachmentAsync(
        DiagnosticAttachment attachment,
        CancellationToken cancellationToken)
    {
        await _sessions.AddAttachmentAsync(attachment, cancellationToken).ConfigureAwait(false);
        _recoveredSnapshot = _sessions.Current ?? _recoveredSnapshot;
    }

    public async Task<bool> RecordProblemMarkerAsync(CancellationToken cancellationToken)
    {
        var recorded = await _sessions.RecordProblemMarkerAsync(cancellationToken).ConfigureAwait(false);
        _recoveredSnapshot = _sessions.Current ?? _recoveredSnapshot;
        return recorded;
    }
}
