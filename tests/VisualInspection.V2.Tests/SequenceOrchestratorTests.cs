using VisualInspection.Core.V2.Configuration;
using VisualInspection.Core.V2.Execution;
using VisualInspection.Infrastructure.V2.Adapters;
using VisualInspection.Infrastructure.V2.Runtime;
using VisualInspection.Runner;

namespace VisualInspection.V2.Tests;

public sealed class SequenceOrchestratorTests
{
    [Fact]
    public async Task ExecutesByInvocationOrder_NotCatalogOrder()
    {
        var project = V2TestFactory.CreateProject(stepCount: 2, reverseCatalog: true);
        var executionOrder = new List<Guid>();
        await using var harness = Harness.Create(project, request =>
        {
            executionOrder.Add(request.Binding.ModelBindingId);
            return Observation(request, 0);
        });
        harness.Camera.Enqueue(V2TestFactory.CreateFrame());

        var result = await harness.Orchestrator.EnqueueAsync(V2TestFactory.CreateRunRequest(project));

        var expected = project.TestSequenceVersions[0].OrderedInvocations
            .Select(invocation => project.TestStepCatalog.Single(step => step.StepId == invocation.StepId).ModelBindings[0].ModelBindingId);
        Assert.Equal(expected, executionOrder);
        Assert.Equal([1, 2], result.Invocations.Select(invocation => invocation.Order));
        Assert.Equal(BusinessVerdict.Pass, result.Verdict);
    }

    [Fact]
    public async Task RepeatedInvocation_SharesFrameAndObservation()
    {
        var project = V2TestFactory.CreateProject();
        var sequence = project.TestSequenceVersions[0];
        var stepId = project.TestStepCatalog[0].StepId;
        project.TestSequenceVersions[0] = sequence with
        {
            OrderedInvocations =
            [
                new StepInvocation { StepId = stepId, Order = 1, CapturePolicy = CapturePolicy.CaptureOncePerProduct },
                new StepInvocation { StepId = stepId, Order = 2, CapturePolicy = CapturePolicy.CaptureOncePerProduct }
            ]
        };
        var modelCalls = 0;
        await using var harness = Harness.Create(project, request =>
        {
            modelCalls++;
            return Observation(request, 0);
        });
        harness.Camera.Enqueue(V2TestFactory.CreateFrame());

        var result = await harness.Orchestrator.EnqueueAsync(V2TestFactory.CreateRunRequest(project));

        Assert.Equal(2, result.Invocations.Count);
        Assert.Equal(1, harness.Camera.CaptureCount);
        Assert.Equal(1, modelCalls);
    }

    [Fact]
    public async Task CapturePerInvocation_AcquiresSeparateFrames()
    {
        var project = V2TestFactory.CreateProject(stepCount: 2);
        var sequence = project.TestSequenceVersions[0];
        project.TestSequenceVersions[0] = sequence with
        {
            OrderedInvocations = sequence.OrderedInvocations.Select(invocation => invocation with
            {
                CapturePolicy = CapturePolicy.CapturePerInvocation
            }).ToList()
        };
        await using var harness = Harness.Create(project, request => Observation(request, 0));
        harness.Camera.Enqueue(V2TestFactory.CreateFrame(frameCounter: 1));
        harness.Camera.Enqueue(V2TestFactory.CreateFrame(frameCounter: 2));

        var result = await harness.Orchestrator.EnqueueAsync(V2TestFactory.CreateRunRequest(project));

        Assert.Equal(BusinessVerdict.Pass, result.Verdict);
        Assert.Equal(2, harness.Camera.CaptureCount);
    }

    [Fact]
    public async Task ContinuousStream_AcquiresAFrameForEachStepInvocation()
    {
        var project = V2TestFactory.CreateProject(stepCount: 2);
        var sequence = project.TestSequenceVersions[0];
        project.TestSequenceVersions[0] = sequence with
        {
            OrderedInvocations = sequence.OrderedInvocations.Select(invocation => invocation with
            {
                CapturePolicy = CapturePolicy.ContinuousStream
            }).ToList()
        };
        await using var harness = Harness.Create(project, request => Observation(request, 0));
        harness.Camera.Enqueue(V2TestFactory.CreateFrame(frameCounter: 1));
        harness.Camera.Enqueue(V2TestFactory.CreateFrame(frameCounter: 2));

        var result = await harness.Orchestrator.EnqueueAsync(V2TestFactory.CreateRunRequest(project));

        Assert.Equal(BusinessVerdict.Pass, result.Verdict);
        Assert.Equal(2, harness.Camera.CaptureCount);
    }

    [Fact]
    public async Task ExternalContextFrame_IsCorrelatedOnceAndSharedWithoutCameraCapture()
    {
        var project = V2TestFactory.CreateProject();
        var sequence = project.TestSequenceVersions[0];
        var stepId = project.TestStepCatalog[0].StepId;
        project.TestSequenceVersions[0] = sequence with
        {
            OrderedInvocations =
            [
                new StepInvocation { StepId = stepId, Order = 1, CapturePolicy = CapturePolicy.ExternalContextFrame },
                new StepInvocation { StepId = stepId, Order = 2, CapturePolicy = CapturePolicy.ExternalContextFrame }
            ]
        };
        await using var harness = Harness.Create(project, request => Observation(request, 0));
        var request = V2TestFactory.CreateRunRequest(project);
        var now = DateTimeOffset.UtcNow;
        request = request with
        {
            ExternalContextFrame = V2TestFactory.CreateFrame(now) with
            {
                TriggerEventId = request.Trigger.EventId,
                ProductId = request.Trigger.ProductId,
                StationId = request.Trigger.StationId,
                HardwareTimestampUtc = now,
                ReceivedAtUtc = now
            }
        };

        var result = await harness.Orchestrator.EnqueueAsync(request);

        Assert.Equal(BusinessVerdict.Pass, result.Verdict);
        Assert.Equal(2, result.Invocations.Count);
        Assert.Equal(0, harness.Camera.CaptureCount);
    }

    [Fact]
    public async Task RequiredFail_MakesSequenceFail()
    {
        var project = V2TestFactory.CreateProject();
        await using var harness = Harness.Create(project, request => Observation(request, 1));
        harness.Camera.Enqueue(V2TestFactory.CreateFrame());

        var result = await harness.Orchestrator.EnqueueAsync(V2TestFactory.CreateRunRequest(project));

        Assert.Equal(RunState.Completed, result.State);
        Assert.Equal(BusinessVerdict.Fail, result.Verdict);
        Assert.False((await harness.Line.GetSnapshotAsync()).Pass);
        Assert.True((await harness.Line.GetSnapshotAsync()).Fail);
    }

    [Fact]
    public async Task OptionalFail_DoesNotFailSequence()
    {
        var project = V2TestFactory.CreateProject();
        var sequence = project.TestSequenceVersions[0];
        project.TestSequenceVersions[0] = sequence with
        {
            OrderedInvocations =
            [
                sequence.OrderedInvocations[0] with
                {
                    IsRequired = false,
                    FailurePolicy = InvocationFailurePolicy.ContinueSequence
                }
            ]
        };
        await using var harness = Harness.Create(project, request => Observation(request, 1));
        harness.Camera.Enqueue(V2TestFactory.CreateFrame());

        var result = await harness.Orchestrator.EnqueueAsync(V2TestFactory.CreateRunRequest(project));

        Assert.Equal(BusinessVerdict.Pass, result.Verdict);
        Assert.Equal(BusinessVerdict.Fail, result.Invocations[0].Verdict);
    }

    [Fact]
    public async Task ModelFailure_IsErroredAndNeverPass()
    {
        var project = V2TestFactory.CreateProject();
        await using var harness = Harness.Create(
            project,
            _ => throw new InvalidOperationException("simulated inference failure"));
        harness.Camera.Enqueue(V2TestFactory.CreateFrame());

        var result = await harness.Orchestrator.EnqueueAsync(V2TestFactory.CreateRunRequest(project));
        var line = await harness.Line.GetSnapshotAsync();

        Assert.Equal(RunState.Errored, result.State);
        Assert.Equal(BusinessVerdict.NotEvaluated, result.Verdict);
        Assert.False(line.Pass);
        Assert.True(line.Error);
    }

    [Fact]
    public async Task Timeout_DiscardsLateUncancellableModelResult()
    {
        var project = V2TestFactory.CreateProject();
        var step = project.TestStepCatalog[0];
        project.TestStepCatalog[0] = step with { DefaultTimeoutMs = 40 };
        var completion = new TaskCompletionSource<ModelObservation>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var harness = Harness.CreateAsync(
            project,
            (_, _) => completion.Task,
            supportsHardCancellation: false);
        harness.Camera.Enqueue(V2TestFactory.CreateFrame());

        var result = await harness.Orchestrator.EnqueueAsync(V2TestFactory.CreateRunRequest(project));
        var beforeLate = await harness.Line.GetSnapshotAsync();
        var binding = project.TestStepCatalog[0].ModelBindings[0];
        completion.SetResult(new ModelObservation
        {
            CaptureId = Guid.NewGuid(),
            ModelBindingId = binding.ModelBindingId,
            ObservedAtUtc = DateTimeOffset.UtcNow,
            Counts = new Dictionary<ModelOutputKey, int>
            {
                [new ModelOutputKey(binding.ModelBindingId, binding.OutputLabelId)] = 0
            }
        });
        await Task.Delay(50);
        var afterLate = await harness.Line.GetSnapshotAsync();

        Assert.Equal(RunState.TimedOut, result.State);
        Assert.Equal(BusinessVerdict.NotEvaluated, result.Verdict);
        Assert.True(beforeLate.Error);
        Assert.False(beforeLate.Pass);
        Assert.Equal(beforeLate, afterLate);
    }

    [Fact]
    public async Task Stop_CancelsActiveRunAndPublishesNoPass()
    {
        var project = V2TestFactory.CreateProject();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var harness = Harness.CreateAsync(project, async (request, cancellationToken) =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return Observation(request, 0);
        });
        harness.Camera.Enqueue(V2TestFactory.CreateFrame());
        var request = V2TestFactory.CreateRunRequest(project);
        var runTask = harness.Orchestrator.EnqueueAsync(request);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await harness.Orchestrator.StopAsync(request.RunId);
        var result = await runTask;
        var line = await harness.Line.GetSnapshotAsync();

        Assert.Equal(RunState.Stopped, result.State);
        Assert.Equal(BusinessVerdict.NotEvaluated, result.Verdict);
        Assert.True(line.Error);
        Assert.False(line.Pass);
    }

    [Fact]
    public async Task DuplicateIdempotencyKey_ReturnsSameRunWithoutSecondCapture()
    {
        var project = V2TestFactory.CreateProject();
        await using var harness = Harness.Create(project, request => Observation(request, 0));
        harness.Camera.Enqueue(V2TestFactory.CreateFrame());
        var request = V2TestFactory.CreateRunRequest(project, idempotencyKey: "same-key");

        var firstTask = harness.Orchestrator.EnqueueAsync(request);
        var duplicateTask = harness.Orchestrator.EnqueueAsync(request);
        var results = await Task.WhenAll(firstTask, duplicateTask);

        Assert.Equal(results[0], results[1]);
        Assert.Equal(1, harness.Camera.CaptureCount);
    }

    [Fact]
    public async Task DuplicateTriggerEventWithoutExplicitKey_IsIdempotent()
    {
        var project = V2TestFactory.CreateProject();
        await using var harness = Harness.Create(project, request => Observation(request, 0));
        harness.Camera.Enqueue(V2TestFactory.CreateFrame());
        var request = V2TestFactory.CreateRunRequest(project);
        request = request with { Trigger = request.Trigger with { IdempotencyKey = string.Empty } };

        var first = harness.Orchestrator.EnqueueAsync(request);
        var duplicate = harness.Orchestrator.EnqueueAsync(request);
        await Task.WhenAll(first, duplicate);

        Assert.Equal(1, harness.Camera.CaptureCount);
    }

    [Fact]
    public async Task BoundedQueue_RejectsNewestWhenFull()
    {
        var project = V2TestFactory.CreateProject();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var harness = Harness.CreateAsync(project, async (request, cancellationToken) =>
        {
            started.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            return Observation(request, 0);
        }, queueCapacity: 1);
        harness.Camera.Enqueue(V2TestFactory.CreateFrame(frameCounter: 1));
        harness.Camera.Enqueue(V2TestFactory.CreateFrame(frameCounter: 2));
        var first = harness.Orchestrator.EnqueueAsync(V2TestFactory.CreateRunRequest(project, idempotencyKey: "first", sequenceNumber: 1));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var second = harness.Orchestrator.EnqueueAsync(V2TestFactory.CreateRunRequest(project, idempotencyKey: "second", sequenceNumber: 2));
        await Task.Delay(20);

        var rejected = await harness.Orchestrator.EnqueueAsync(
            V2TestFactory.CreateRunRequest(project, idempotencyKey: "third", sequenceNumber: 3));
        release.TrySetResult();
        await first;
        await second;

        Assert.Equal(RunState.Errored, rejected.State);
        Assert.Equal("RUN_QUEUE_FULL", rejected.Error?.Code);
        Assert.Equal(BusinessVerdict.NotEvaluated, rejected.Verdict);
    }

    private static ModelObservation Observation(ModelExecutionRequest request, int count) => new()
    {
        CaptureId = request.Frame.CaptureId,
        ModelBindingId = request.Binding.ModelBindingId,
        ObservedAtUtc = DateTimeOffset.UtcNow,
        Counts = new Dictionary<ModelOutputKey, int>
        {
            [new ModelOutputKey(request.Binding.ModelBindingId, request.Binding.OutputLabelId)] = count
        },
        FrameWidth = request.Frame.ImageFrame.Width,
        FrameHeight = request.Frame.ImageFrame.Height,
        ProviderDetails = "simulator"
    };

    private sealed class Harness : IAsyncDisposable
    {
        private Harness(
            SequenceOrchestrator orchestrator,
            SimulatedCameraAdapter camera,
            SimulatedLineResultAdapter line)
        {
            Orchestrator = orchestrator;
            Camera = camera;
            Line = line;
        }

        public SequenceOrchestrator Orchestrator { get; }
        public SimulatedCameraAdapter Camera { get; }
        public SimulatedLineResultAdapter Line { get; }

        public static Harness Create(
            ProjectConfigurationV2 project,
            Func<ModelExecutionRequest, ModelObservation> execute,
            int queueCapacity = 8) =>
            CreateAsync(project, (request, _) => Task.FromResult(execute(request)), queueCapacity: queueCapacity);

        public static Harness CreateAsync(
            ProjectConfigurationV2 project,
            Func<ModelExecutionRequest, CancellationToken, Task<ModelObservation>> execute,
            bool supportsHardCancellation = true,
            int queueCapacity = 8)
        {
            var adapterId = project.ModelArtifacts[0].AdapterId;
            var model = new SimulatedModelRuntimeAdapter(
                adapterId,
                TestStepKind.Detection,
                execute,
                supportsHardCancellation: supportsHardCancellation);
            var camera = new SimulatedCameraAdapter();
            var line = new SimulatedLineResultAdapter();
            var orchestrator = new SequenceOrchestrator(
                new AlwaysReadyGate([new ProviderRecord(adapterId, RuntimeProviderKind.Simulator, supportsHardCancellation)]),
                new ModelRuntimeAdapterRegistry([model]),
                new CameraAdapterRegistry([camera]),
                new LineResultAdapterRegistry([line]),
                new SequenceOrchestratorOptions
                {
                    QueueCapacity = queueCapacity,
                    MaxInFlightRuns = 1,
                    OverflowPolicy = QueueOverflowPolicy.RejectNewest,
                    BaseDirectory = Directory.GetCurrentDirectory()
                });
            return new Harness(orchestrator, camera, line);
        }

        public ValueTask DisposeAsync() => Orchestrator.DisposeAsync();
    }
}
