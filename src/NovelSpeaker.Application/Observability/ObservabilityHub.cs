namespace NovelSpeaker.Application.Observability;

/// <summary>
/// Fans out one instrumentation point to independent consumers without making them a business dependency.
/// </summary>
public sealed class ObservabilityHub : IObservability
{
    private readonly IObservabilityContextAccessor _contextAccessor;
    private readonly IObservabilityConsumer[] _consumers;
    private readonly TimeProvider _timeProvider;

    public ObservabilityHub(
        IObservabilityContextAccessor contextAccessor,
        IEnumerable<IObservabilityConsumer>? consumers = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(contextAccessor);

        _contextAccessor = contextAccessor;
        _consumers = (consumers ?? []).ToArray();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public bool IsEnabled => _consumers.Length != 0;

    public CorrelationContext CurrentContext => _contextAccessor.Current;

    public IOperationScope StartOperation(OperationDefinition operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (!IsEnabled)
        {
            return new NoOpOperationScope(operation, _contextAccessor.Current);
        }

        var startedAt = _timeProvider.GetUtcNow();
        var currentContext = _contextAccessor.Current;
        var context = new CorrelationContext(
            currentContext.ProcessInstanceId,
            currentContext.DiagnosticSessionId,
            Guid.NewGuid().ToString("N"));
        var correlationScope = _contextAccessor.Push(context);
        var started = new OperationStarted(operation, context, startedAt)
        {
            ParentActivityId = currentContext.ActivityId
        };
        Notify(consumer => consumer.OnOperationStarted(started));
        return new ActiveOperationScope(this, operation, context, startedAt, correlationScope);
    }

    public void Record(DiagnosticDefinition definition, DiagnosticFieldSet fields)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(fields);
        if (!IsEnabled)
        {
            return;
        }

        var diagnosticEvent = new DiagnosticEvent(
            definition,
            fields,
            _contextAccessor.Current,
            _timeProvider.GetUtcNow());
        Notify(consumer => consumer.OnDiagnosticEvent(diagnosticEvent));
    }

    private void Complete(
        OperationDefinition operation,
        CorrelationContext context,
        DateTimeOffset startedAt,
        OperationResult result)
    {
        var completed = new OperationCompleted(
            operation,
            context,
            startedAt,
            _timeProvider.GetUtcNow(),
            result);
        Notify(consumer => consumer.OnOperationCompleted(completed));
    }

    private void Notify(Action<IObservabilityConsumer> callback)
    {
        foreach (var consumer in _consumers)
        {
            try
            {
                callback(consumer);
            }
            catch
            {
                // Observability is best effort. A broken consumer must not alter business control flow.
            }
        }
    }

    private sealed class ActiveOperationScope : IOperationScope
    {
        private readonly ObservabilityHub _owner;
        private readonly IDisposable _correlationScope;
        private readonly OperationDefinition _operation;
        private readonly CorrelationContext _context;
        private readonly DateTimeOffset _startedAt;
        private int _completed;

        public ActiveOperationScope(
            ObservabilityHub owner,
            OperationDefinition operation,
            CorrelationContext context,
            DateTimeOffset startedAt,
            IDisposable correlationScope)
        {
            _owner = owner;
            _operation = operation;
            _context = context;
            _startedAt = startedAt;
            _correlationScope = correlationScope;
        }

        public OperationDefinition Operation => _operation;

        public CorrelationContext Context => _context;

        public bool IsCompleted => Volatile.Read(ref _completed) != 0;

        public void Complete(OperationResult result)
        {
            if (Interlocked.Exchange(ref _completed, 1) != 0)
            {
                return;
            }

            try
            {
                _owner.Complete(_operation, _context, _startedAt, result);
            }
            finally
            {
                _correlationScope.Dispose();
            }
        }

        public void Dispose() => Complete(OperationResult.Cancelled());
    }

    private sealed class NoOpOperationScope : IOperationScope
    {
        private readonly OperationDefinition _operation;
        private readonly CorrelationContext _context;
        private int _completed;

        public NoOpOperationScope(OperationDefinition operation, CorrelationContext context)
        {
            _operation = operation;
            _context = context;
        }

        public OperationDefinition Operation => _operation;

        public CorrelationContext Context => _context;

        public bool IsCompleted => Volatile.Read(ref _completed) != 0;

        public void Complete(OperationResult result)
        {
            Interlocked.Exchange(ref _completed, 1);
        }

        public void Dispose() => Complete(OperationResult.Cancelled());
    }
}
