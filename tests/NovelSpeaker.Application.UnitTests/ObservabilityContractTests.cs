using NovelSpeaker.Application.Observability;
using Xunit;

namespace NovelSpeaker.Application.UnitTests;

public sealed class ObservabilityContractTests
{
    [Fact]
    public void Operation_catalog_has_unique_stable_ids()
    {
        var operations = OperationCatalog.All;

        Assert.Equal(operations.Count, operations.Select(operation => operation.Id).Distinct().Count());
        Assert.All(
            operations,
            operation =>
            {
                Assert.Equal(operation.Id.Value, operation.Id.Value.ToLowerInvariant());
                Assert.DoesNotContain(operation.Id.Value, value => char.IsWhiteSpace(value));
            });
    }

    [Fact]
    public void Diagnostic_registry_has_unique_ids_and_declared_fields_only()
    {
        var registry = DiagnosticRegistry.Default;

        Assert.Equal(registry.Definitions.Count, registry.Definitions.Select(definition => definition.Id).Distinct().Count());
        var definition = registry.Get(new DiagnosticDefinitionId("tts.retry"));
        var field = Assert.Single(definition.Fields);

        var set = definition.CreateFields(DiagnosticFieldValue.Integer(field, 2));

        Assert.Same(field, Assert.Single(set.Values).Field);
        Assert.Throws<ArgumentException>(() =>
            DiagnosticFieldSet.Create(
                definition,
                [DiagnosticFieldValue.Integer(new DiagnosticFieldDefinition(
                    "other",
                    DiagnosticFieldType.Integer,
                    DiagnosticPrivacyClass.Technical,
                    "Not declared."), 1)]));
    }

    [Fact]
    public void Forbidden_content_fields_cannot_be_registered()
    {
        var field = new DiagnosticFieldDefinition(
            "bookTitle",
            DiagnosticFieldType.String,
            DiagnosticPrivacyClass.UserContent,
            "User supplied title.",
            ["redacted"]);

        Assert.Throws<ArgumentException>(() => new DiagnosticDefinition(
            new DiagnosticDefinitionId("invalid.content"),
            DiagnosticDefinitionKind.Event,
            "test",
            "Invalid definition.",
            [field]));
    }

    [Fact]
    public void Hub_without_consumers_is_safe_no_op()
    {
        var context = new ObservabilityContextAccessor("process-1");
        var hub = new ObservabilityHub(context);

        Assert.False(hub.IsEnabled);
        using var operation = hub.StartOperation(OperationCatalog.AppStartup);
        hub.Record(
            DiagnosticRegistry.Default.Get(new DiagnosticDefinitionId("app.lifecycle")),
            DiagnosticRegistry.Default.Get(new DiagnosticDefinitionId("app.lifecycle"))
                .CreateFields(DiagnosticFieldValue.Enum(
                    DiagnosticRegistry.Default.Get(new DiagnosticDefinitionId("app.lifecycle")).Fields[0],
                    "startup")));
        operation.Complete(OperationResult.Succeeded());
        Assert.True(operation.IsCompleted);
        operation.Dispose();
        Assert.True(operation.IsCompleted);
    }

    [Fact]
    public void A_failing_consumer_does_not_block_other_consumers_or_business_callers()
    {
        var context = new ObservabilityContextAccessor("process-1");
        var healthy = new RecordingConsumer();
        var hub = new ObservabilityHub(context, [new ThrowingConsumer(), healthy]);

        using (var operation = hub.StartOperation(OperationCatalog.PlaybackStart))
        {
            Assert.Equal("process-1", operation.Context.ProcessInstanceId);
            Assert.NotNull(operation.Context.ActivityId);
            operation.Complete(OperationResult.Succeeded());
        }

        Assert.Single(healthy.Started);
        Assert.Single(healthy.Completed);
    }

    [Fact]
    public async Task Correlation_scope_is_restored_after_async_operation_and_does_not_leak()
    {
        var context = new ObservabilityContextAccessor("process-1");
        var scoped = new CorrelationContext("process-1", "session-1", "activity-1");

        using (context.Push(scoped))
        {
            Assert.Equal("session-1", context.Current.DiagnosticSessionId);
            await Task.CompletedTask;
            Assert.Equal("activity-1", context.Current.ActivityId);
        }

        Assert.Equal("process-1", context.Current.ProcessInstanceId);
        Assert.Null(context.Current.DiagnosticSessionId);
        Assert.Null(context.Current.ActivityId);
        var observed = await Task.Run(() => context.Current);
        Assert.Null(observed.DiagnosticSessionId);
        Assert.Null(observed.ActivityId);
    }

    [Fact]
    public void Correlation_scope_does_not_restore_a_disposed_outer_scope()
    {
        var context = new ObservabilityContextAccessor("process-1");
        using var outer = context.Push(new CorrelationContext("process-1", "session-1"));
        using var inner = context.Push(new CorrelationContext("process-1", "session-2"));

        outer.Dispose();

        Assert.Equal("process-1", context.Current.ProcessInstanceId);
        Assert.Equal("session-2", context.Current.DiagnosticSessionId);
    }

    [Fact]
    public void Unbounded_string_fields_are_not_valid_structured_contracts()
    {
        Assert.Throws<ArgumentException>(() => new DiagnosticFieldDefinition(
            "provider",
            DiagnosticFieldType.String,
            DiagnosticPrivacyClass.Technical,
            "Unbounded provider value."));
    }

    private sealed class RecordingConsumer : IObservabilityConsumer
    {
        public List<OperationStarted> Started { get; } = [];

        public List<OperationCompleted> Completed { get; } = [];

        public void OnOperationStarted(OperationStarted operation) => Started.Add(operation);

        public void OnOperationCompleted(OperationCompleted operation) => Completed.Add(operation);

        public void OnDiagnosticEvent(DiagnosticEvent diagnosticEvent)
        {
        }
    }

    private sealed class ThrowingConsumer : IObservabilityConsumer
    {
        public void OnOperationStarted(OperationStarted operation) => throw new InvalidOperationException();

        public void OnOperationCompleted(OperationCompleted operation) => throw new InvalidOperationException();

        public void OnDiagnosticEvent(DiagnosticEvent diagnosticEvent) => throw new InvalidOperationException();
    }
}
