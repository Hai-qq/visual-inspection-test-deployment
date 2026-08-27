using VisualInspection.Core.V2.Configuration;
using VisualInspection.Core.V2.Execution;
using VisualInspection.Runner;

namespace VisualInspection.V2.Tests;

public sealed class TriggerDispatchServiceTests
{
    [Fact]
    public async Task DropOldest_RejectsQueuedOldestAndRunsNewest()
    {
        var project = V2TestFactory.CreateProject();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var dispatch = new TriggerDispatchService(1, 1, QueueOverflowPolicy.DropOldest, async (request, cancellationToken) =>
        {
            if (request.Trigger.IdempotencyKey == "first")
            {
                started.TrySetResult();
                await release.Task.WaitAsync(cancellationToken);
            }

            return Completed(request);
        });
        var first = Work(project, "first", 1);
        var second = Work(project, "second", 2);
        var third = Work(project, "third", 3);
        await dispatch.EnqueueAsync(first);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await dispatch.EnqueueAsync(second);
        await dispatch.EnqueueAsync(third);

        var dropped = await second.Completion.WaitAsync(TimeSpan.FromSeconds(2));
        release.TrySetResult();
        await Task.WhenAll(first.Completion, third.Completion);

        Assert.Equal("RUN_QUEUE_DROPPED", dropped.Error?.Code);
        Assert.Equal(BusinessVerdict.NotEvaluated, dropped.Verdict);
        Assert.Equal(BusinessVerdict.Pass, (await third.Completion).Verdict);
    }

    [Fact]
    public async Task Wait_BackpressuresWriterUntilCapacityIsAvailable()
    {
        var project = V2TestFactory.CreateProject();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var dispatch = new TriggerDispatchService(1, 1, QueueOverflowPolicy.Wait, async (request, cancellationToken) =>
        {
            if (request.Trigger.IdempotencyKey == "first")
            {
                started.TrySetResult();
                await release.Task.WaitAsync(cancellationToken);
            }

            return Completed(request);
        });
        var first = Work(project, "first", 1);
        var second = Work(project, "second", 2);
        var third = Work(project, "third", 3);
        await dispatch.EnqueueAsync(first);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await dispatch.EnqueueAsync(second);
        var waitingWrite = dispatch.EnqueueAsync(third);

        Assert.False(waitingWrite.IsCompleted);
        release.TrySetResult();
        await waitingWrite.WaitAsync(TimeSpan.FromSeconds(2));
        var results = await Task.WhenAll(first.Completion, second.Completion, third.Completion);
        Assert.All(results, result => Assert.Equal(BusinessVerdict.Pass, result.Verdict));
    }

    private static DispatchWorkItem Work(ProjectConfigurationV2 project, string key, long sequenceNumber) =>
        new(V2TestFactory.CreateRunRequest(project, idempotencyKey: key, sequenceNumber: sequenceNumber));

    private static SequenceRunResult Completed(SequenceRunRequest request)
    {
        var now = DateTimeOffset.UtcNow;
        return new SequenceRunResult
        {
            RunId = request.RunId,
            State = RunState.Completed,
            Verdict = BusinessVerdict.Pass,
            StartedAtUtc = now,
            CompletedAtUtc = now,
            Invocations = [],
            TestRecord = new TestRecord
            {
                RunId = request.RunId,
                EnvironmentMode = request.EnvironmentMode,
                Providers = [],
                SequenceId = request.SequenceId,
                SequenceVersion = request.SequenceVersion,
                TriggerSequenceNumber = request.Trigger.SequenceNumber,
                IdempotencyKey = request.Trigger.IdempotencyKey,
                StartedAtUtc = now,
                CompletedAtUtc = now
            }
        };
    }
}
