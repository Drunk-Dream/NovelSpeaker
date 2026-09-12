using System.Threading;

namespace NovelSpeaker.Application.Observability;

/// <summary>
/// Carries observability context through an async flow and restores it when the scope ends.
/// </summary>
public sealed class ObservabilityContextAccessor : IObservabilityContextAccessor
{
    private readonly AsyncLocal<ScopeFrame?> _current = new();
    private readonly CorrelationContext _processContext;
    private string? _diagnosticSessionId;

    public ObservabilityContextAccessor(string? processInstanceId = null)
    {
        _processContext = new CorrelationContext(
            processInstanceId ?? Guid.NewGuid().ToString("N"));
    }

    public CorrelationContext Current => GetEffectiveContext(_current.Value);

    public void SetDiagnosticSession(string? diagnosticSessionId)
    {
        if (diagnosticSessionId is not null)
        {
            _ = new CorrelationContext(_processContext.ProcessInstanceId, diagnosticSessionId);
        }

        Volatile.Write(ref _diagnosticSessionId, diagnosticSessionId);
    }

    public IDisposable Push(CorrelationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!context.ProcessInstanceId.Equals(_processContext.ProcessInstanceId, StringComparison.Ordinal))
        {
            throw new ArgumentException("A scope cannot replace the process correlation.", nameof(context));
        }

        var frame = new ScopeFrame(context, _current.Value);
        _current.Value = frame;
        return new RestoreScope(this, frame);
    }

    private CorrelationContext GetEffectiveContext(ScopeFrame? frame)
    {
        while (frame is not null)
        {
            if (!frame.IsDisposed)
            {
                var diagnosticSessionId = frame.Context.DiagnosticSessionId ?? Volatile.Read(ref _diagnosticSessionId);
                return diagnosticSessionId is null ||
                       string.Equals(diagnosticSessionId, frame.Context.DiagnosticSessionId, StringComparison.Ordinal)
                    ? frame.Context
                    : new CorrelationContext(
                        frame.Context.ProcessInstanceId,
                        diagnosticSessionId,
                        frame.Context.ActivityId);
            }

            frame = frame.Parent;
        }

        return new CorrelationContext(
            _processContext.ProcessInstanceId,
            Volatile.Read(ref _diagnosticSessionId));
    }

    private sealed class ScopeFrame
    {
        private int _disposed;

        public ScopeFrame(CorrelationContext context, ScopeFrame? parent)
        {
            Context = context;
            Parent = parent;
        }

        public CorrelationContext Context { get; }

        public ScopeFrame? Parent { get; }

        public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

        public void Dispose() => Interlocked.Exchange(ref _disposed, 1);
    }

    private sealed class RestoreScope : IDisposable
    {
        private readonly ObservabilityContextAccessor _owner;
        private readonly ScopeFrame _frame;
        private int _disposed;

        public RestoreScope(ObservabilityContextAccessor owner, ScopeFrame frame)
        {
            _owner = owner;
            _frame = frame;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _frame.Dispose();
                if (ReferenceEquals(_owner._current.Value, _frame))
                {
                    _owner._current.Value = _frame.Parent;
                }
            }
        }
    }
}
