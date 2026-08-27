using System.Collections.Concurrent;
using VisualInspection.Core.V2.Configuration;
using VisualInspection.Core.V2.Execution;

namespace VisualInspection.Runner;

public sealed class SequenceOrchestrator : ISequenceOrchestrator
{
    private readonly ISequenceReadinessGate _readinessGate;
    private readonly StepExecutor _stepExecutor;
    private readonly CaptureCoordinator _captureCoordinator;
    private readonly ResultPublicationCoordinator _publicationCoordinator;
    private readonly TriggerDispatchService _dispatch;
    private readonly TimeProvider _timeProvider;
    private readonly int _idempotencyRetentionCapacity;
    private readonly ConcurrentDictionary<string, Task<SequenceRunResult>> _idempotency =
        new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> _completedIdempotencyKeys = new();
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _runCancellations = new();
    private bool _disposed;

    public SequenceOrchestrator(
        ISequenceReadinessGate readinessGate,
        IModelRuntimeAdapterRegistry modelAdapters,
        ICameraAdapterRegistry cameraAdapters,
        ILineResultAdapterRegistry lineAdapters,
        SequenceOrchestratorOptions? options = null,
        TimeProvider? timeProvider = null)
    {
        _readinessGate = readinessGate ?? throw new ArgumentNullException(nameof(readinessGate));
        options ??= new SequenceOrchestratorOptions();
        if (options.QueueCapacity <= 0 || options.MaxInFlightRuns <= 0 ||
            options.IdempotencyRetentionCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }

        _timeProvider = timeProvider ?? TimeProvider.System;
        _idempotencyRetentionCapacity = options.IdempotencyRetentionCapacity;
        _captureCoordinator = new CaptureCoordinator(cameraAdapters, timeProvider: _timeProvider);
        _stepExecutor = new StepExecutor(modelAdapters, _captureCoordinator, options.BaseDirectory, _timeProvider);
        _publicationCoordinator = new ResultPublicationCoordinator(lineAdapters);
        _dispatch = new TriggerDispatchService(
            options.QueueCapacity,
            options.MaxInFlightRuns,
            options.OverflowPolicy,
            ExecuteQueuedAsync);
    }

    public async Task<SequenceReadinessResult> PrepareAsync(
        ProjectConfigurationV2 project,
        Guid sequenceId,
        string sequenceVersion,
        Guid deploymentBindingId,
        RuntimeEnvironmentMode environmentMode,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(sequenceVersion);
        var sequence = ResolveSequence(project, sequenceId, sequenceVersion);
        var deployment = ResolveDeployment(project, deploymentBindingId);
        var readiness = await _readinessGate.EvaluateAsync(
            project,
            sequence,
            deployment,
            environmentMode,
            cancellationToken);
        try
        {
            await _publicationCoordinator.SetReadyAsync(deployment, readiness.IsReady, cancellationToken);
        }
        catch when (!readiness.IsReady)
        {
            // A fail-closed adapter can reject all writes while still remaining NotReady.
        }

        return readiness;
    }

    public async Task<SequenceRunResult> EnqueueAsync(
        SequenceRunRequest request,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);
        var key = GetIdempotencyKey(request.Trigger);
        if (_idempotency.TryGetValue(key, out var existing))
        {
            return await existing.WaitAsync(cancellationToken);
        }

        var cancellation = new CancellationTokenSource();
        if (!_runCancellations.TryAdd(request.RunId, cancellation))
        {
            cancellation.Dispose();
            throw new InvalidOperationException($"RunId {request.RunId} 重复。");
        }

        var workItem = new DispatchWorkItem(request);
        if (!_idempotency.TryAdd(key, workItem.Completion))
        {
            _runCancellations.TryRemove(request.RunId, out _);
            cancellation.Dispose();
            return await _idempotency[key].WaitAsync(cancellationToken);
        }

        _ = workItem.Completion.ContinueWith(
            _ => CompleteRun(key, request.RunId, cancellation),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        await _dispatch.EnqueueAsync(workItem, cancellationToken);
        return await workItem.Completion.WaitAsync(cancellationToken);
    }

    public Task StopAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_runCancellations.TryGetValue(runId, out var cancellation))
        {
            cancellation.Cancel();
            _captureCoordinator.CancelRun(runId);
        }

        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var cancellation in _runCancellations.Values)
        {
            cancellation.Cancel();
        }

        await _dispatch.DisposeAsync();
        foreach (var cancellation in _runCancellations.Values)
        {
            cancellation.Dispose();
        }

        _runCancellations.Clear();
    }

    private async Task<SequenceRunResult> ExecuteQueuedAsync(
        SequenceRunRequest request,
        CancellationToken dispatcherCancellation)
    {
        var startedAt = _timeProvider.GetUtcNow();
        if (startedAt >= request.DeadlineUtc)
        {
            return CreateTerminal(
                request,
                RunState.TimedOut,
                BusinessVerdict.NotEvaluated,
                startedAt,
                [],
                new StructuredError
                {
                    Category = ErrorCategory.Timeout,
                    Code = "RUN_QUEUE_DEADLINE",
                    Message = "Run 在队列中等待期间已超过 Deadline。"
                },
                []);
        }

        TestSequenceVersion sequence;
        DeploymentBinding deployment;
        try
        {
            sequence = ResolveSequence(request.Project, request.SequenceId, request.SequenceVersion);
            deployment = ResolveDeployment(request.Project, request.DeploymentBindingId);
        }
        catch (Exception exception)
        {
            return CreateTerminal(
                request,
                RunState.Errored,
                BusinessVerdict.NotEvaluated,
                startedAt,
                [],
                new StructuredError
                {
                    Category = ErrorCategory.Configuration,
                    Code = "RUN_CONFIGURATION_REFERENCE",
                    Message = exception.Message
                },
                []);
        }

        SequenceReadinessResult readiness;
        try
        {
            readiness = await PrepareAsync(
                request.Project,
                request.SequenceId,
                request.SequenceVersion,
                request.DeploymentBindingId,
                request.EnvironmentMode,
                dispatcherCancellation);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return CreateTerminal(
                request,
                RunState.Errored,
                BusinessVerdict.NotEvaluated,
                startedAt,
                [],
                new StructuredError
                {
                    Category = ErrorCategory.LineCommunication,
                    Code = "RUN_PREPARE_FAILED",
                    Message = exception.Message
                },
                []);
        }

        if (!readiness.IsReady)
        {
            var issue = readiness.Issues.First(value => value.Severity == V2ValidationSeverity.Error);
            return CreateTerminal(
                request,
                RunState.Errored,
                BusinessVerdict.NotEvaluated,
                startedAt,
                [],
                new StructuredError
                {
                    Category = ErrorCategory.Configuration,
                    Code = issue.Code,
                    Message = issue.Message
                },
                readiness.Providers);
        }

        if (!_runCancellations.TryGetValue(request.RunId, out var runCancellation))
        {
            return CreateTerminal(
                request,
                RunState.Stopped,
                BusinessVerdict.NotEvaluated,
                startedAt,
                [],
                new StructuredError
                {
                    Category = ErrorCategory.Cancellation,
                    Code = "RUN_CANCELLED_BEFORE_START",
                    Message = "Run 在启动前已取消。"
                },
                readiness.Providers);
        }

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            dispatcherCancellation,
            runCancellation.Token);
        try
        {
            await _publicationCoordinator.BeginAsync(deployment, request.RunId, linkedCancellation.Token);
        }
        catch (Exception exception)
        {
            return CreateTerminal(
                request,
                RunState.Errored,
                BusinessVerdict.NotEvaluated,
                startedAt,
                [],
                new StructuredError
                {
                    Category = ErrorCategory.LineCommunication,
                    Code = "LINE_BEGIN_FAILED",
                    Message = exception.Message
                },
                readiness.Providers);
        }

        var invocationResults = new List<StepInvocationResult>();
        var captureContext = _captureCoordinator.CreateContext(request, deployment);
        var observationCache = new RunObservationCache();
        try
        {
            var steps = request.Project.TestStepCatalog.ToDictionary(step => step.StepId);
            foreach (var invocation in sequence.OrderedInvocations.OrderBy(value => value.Order))
            {
                linkedCancellation.Token.ThrowIfCancellationRequested();
                if (_timeProvider.GetUtcNow() >= request.DeadlineUtc)
                {
                    invocationResults.Add(CreateInvocationDeadline(invocation));
                    break;
                }

                if (invocation.DelayMs > 0)
                {
                    var remaining = request.DeadlineUtc - _timeProvider.GetUtcNow();
                    var delay = TimeSpan.FromMilliseconds(invocation.DelayMs);
                    if (delay >= remaining)
                    {
                        invocationResults.Add(CreateInvocationDeadline(invocation));
                        break;
                    }

                    await Task.Delay(delay, _timeProvider, linkedCancellation.Token);
                }

                if (!steps.TryGetValue(invocation.StepId, out var step))
                {
                    invocationResults.Add(new StepInvocationResult
                    {
                        InvocationId = invocation.InvocationId,
                        StepId = invocation.StepId,
                        Order = invocation.Order,
                        IsRequired = invocation.IsRequired,
                        State = RunState.Errored,
                        Verdict = BusinessVerdict.NotEvaluated,
                        Error = new StructuredError
                        {
                            Category = ErrorCategory.Configuration,
                            Code = "STEP_NOT_FOUND",
                            Message = "Invocation 引用的 Test Step 不存在。"
                        },
                        Summary = "Invocation 引用的 Test Step 不存在。"
                    });
                    break;
                }

                var result = await _stepExecutor.ExecuteAsync(
                    request,
                    sequence,
                    step,
                    invocation,
                    captureContext,
                    observationCache,
                    linkedCancellation.Token);
                invocationResults.Add(result);
                if (result.State != RunState.Completed ||
                    result.Verdict == BusinessVerdict.Fail && invocation.FailurePolicy == InvocationFailurePolicy.StopSequence)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            invocationResults.Add(new StepInvocationResult
            {
                InvocationId = Guid.NewGuid(),
                StepId = Guid.Empty,
                Order = invocationResults.Count + 1,
                IsRequired = true,
                State = RunState.Stopped,
                Verdict = BusinessVerdict.NotEvaluated,
                Error = new StructuredError
                {
                    Category = ErrorCategory.Cancellation,
                    Code = "RUN_STOPPED",
                    Message = "Run 已停止。"
                },
                Summary = "Run 已停止。"
            });
        }

        var resultToPublish = Aggregate(request, startedAt, invocationResults, readiness.Providers);
        if (resultToPublish.State is RunState.TimedOut or RunState.Stopped or RunState.Errored)
        {
            _captureCoordinator.CancelRun(request.RunId);
        }

        try
        {
            using var publicationTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            var published = await _publicationCoordinator.PublishAsync(
                deployment,
                resultToPublish,
                request.Trigger.SequenceNumber,
                publicationTimeout.Token);
            if (!published)
            {
                return resultToPublish with
                {
                    State = RunState.Errored,
                    Verdict = BusinessVerdict.NotEvaluated,
                    Error = new StructuredError
                    {
                        Category = ErrorCategory.LineCommunication,
                        Code = "LATE_RESULT_DISCARDED",
                        Message = "Run 已不是当前活动 Run，迟到结果未发布。"
                    }
                };
            }
        }
        catch (Exception exception)
        {
            var error = new StructuredError
            {
                Category = ErrorCategory.LineCommunication,
                Code = "LINE_RESULT_PUBLICATION_FAILED",
                Message = exception.Message
            };
            try
            {
                await _publicationCoordinator.FaultAndNotReadyAsync(deployment, error, CancellationToken.None);
            }
            catch
            {
            }

            return resultToPublish with
            {
                State = RunState.Errored,
                Verdict = BusinessVerdict.NotEvaluated,
                Error = error
            };
        }

        return resultToPublish;
    }

    private SequenceRunResult Aggregate(
        SequenceRunRequest request,
        DateTimeOffset startedAt,
        IReadOnlyList<StepInvocationResult> invocations,
        IReadOnlyList<ProviderRecord> providers)
    {
        var state = invocations.Any(result => result.State == RunState.Stopped)
            ? RunState.Stopped
            : invocations.Any(result => result.State == RunState.TimedOut)
                ? RunState.TimedOut
                : invocations.Count == 0 || invocations.Any(result => result.State == RunState.Errored)
                    ? RunState.Errored
                    : RunState.Completed;
        var verdict = state != RunState.Completed
            ? BusinessVerdict.NotEvaluated
            : invocations.Any(result => result.IsRequired && result.Verdict == BusinessVerdict.Fail)
                ? BusinessVerdict.Fail
                : BusinessVerdict.Pass;
        var error = state == RunState.Completed
            ? null
            : invocations.LastOrDefault(result => result.Error is not null)?.Error ?? new StructuredError
            {
                Category = ErrorCategory.Resource,
                Code = "RUN_EMPTY",
                Message = "Run 没有产生可评估 Invocation。"
            };
        return CreateTerminal(request, state, verdict, startedAt, invocations, error, providers);
    }

    private SequenceRunResult CreateTerminal(
        SequenceRunRequest request,
        RunState state,
        BusinessVerdict verdict,
        DateTimeOffset startedAt,
        IReadOnlyList<StepInvocationResult> invocations,
        StructuredError? error,
        IReadOnlyList<ProviderRecord> providers)
    {
        var completedAt = _timeProvider.GetUtcNow();
        return new SequenceRunResult
        {
            RunId = request.RunId,
            State = state,
            Verdict = verdict,
            StartedAtUtc = startedAt,
            CompletedAtUtc = completedAt,
            Invocations = invocations,
            Error = error,
            TestRecord = new TestRecord
            {
                RunId = request.RunId,
                EnvironmentMode = request.EnvironmentMode,
                Providers = providers,
                SequenceId = request.SequenceId,
                SequenceVersion = request.SequenceVersion,
                TriggerSequenceNumber = request.Trigger.SequenceNumber,
                IdempotencyKey = request.Trigger.IdempotencyKey,
                StartedAtUtc = startedAt,
                CompletedAtUtc = completedAt
            }
        };
    }

    private static StepInvocationResult CreateInvocationDeadline(StepInvocation invocation) => new()
    {
        InvocationId = invocation.InvocationId,
        StepId = invocation.StepId,
        Order = invocation.Order,
        IsRequired = invocation.IsRequired,
        State = RunState.TimedOut,
        Verdict = BusinessVerdict.NotEvaluated,
        Error = new StructuredError
        {
            Category = ErrorCategory.Timeout,
            Code = "RUN_DEADLINE",
            Message = "Run Deadline 已到。"
        },
        Summary = "Run Deadline 已到。"
    };

    private void CompleteRun(string idempotencyKey, Guid runId, CancellationTokenSource cancellation)
    {
        _runCancellations.TryRemove(runId, out _);
        cancellation.Dispose();
        _completedIdempotencyKeys.Enqueue(idempotencyKey);
        while (_completedIdempotencyKeys.Count > _idempotencyRetentionCapacity &&
               _completedIdempotencyKeys.TryDequeue(out var expired))
        {
            _idempotency.TryRemove(expired, out _);
        }
    }

    private static string GetIdempotencyKey(TriggerEvent trigger) =>
        string.IsNullOrWhiteSpace(trigger.IdempotencyKey) ? $"event:{trigger.EventId:N}" : trigger.IdempotencyKey;

    private static void ValidateRequest(SequenceRunRequest request)
    {
        if (request.RunId == Guid.Empty || request.SequenceId == Guid.Empty || request.DeploymentBindingId == Guid.Empty ||
            request.Trigger.EventId == Guid.Empty)
        {
            throw new ArgumentException("Run、Sequence、Deployment 和 Trigger Event ID 均不能为空。", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.SequenceVersion) || string.IsNullOrWhiteSpace(request.Trigger.ProductId) ||
            string.IsNullOrWhiteSpace(request.Trigger.StationId))
        {
            throw new ArgumentException("Sequence Version、ProductId 和 StationId 均不能为空。", nameof(request));
        }
    }

    private static TestSequenceVersion ResolveSequence(ProjectConfigurationV2 project, Guid id, string version) =>
        project.TestSequenceVersions.SingleOrDefault(sequence =>
            sequence.SequenceId == id && string.Equals(sequence.Version, version, StringComparison.Ordinal))
        ?? throw new KeyNotFoundException($"Sequence {id} v{version} 不存在。");

    private static DeploymentBinding ResolveDeployment(ProjectConfigurationV2 project, Guid id) =>
        project.DeploymentBindings.SingleOrDefault(deployment => deployment.DeploymentBindingId == id)
        ?? throw new KeyNotFoundException($"Deployment Binding {id} 不存在。");
}
