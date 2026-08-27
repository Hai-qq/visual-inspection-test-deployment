using VisualInspection.Core.V2.Configuration;
using VisualInspection.Core.V2.Execution;
using VisualInspection.Core.V2.Rules;

namespace VisualInspection.Runner;

public sealed class StepExecutor
{
    private readonly IModelRuntimeAdapterRegistry _modelAdapters;
    private readonly CaptureCoordinator _captureCoordinator;
    private readonly TimeProvider _timeProvider;
    private readonly string _baseDirectory;

    public StepExecutor(
        IModelRuntimeAdapterRegistry modelAdapters,
        CaptureCoordinator captureCoordinator,
        string baseDirectory,
        TimeProvider? timeProvider = null)
    {
        _modelAdapters = modelAdapters ?? throw new ArgumentNullException(nameof(modelAdapters));
        _captureCoordinator = captureCoordinator ?? throw new ArgumentNullException(nameof(captureCoordinator));
        _baseDirectory = Path.GetFullPath(baseDirectory);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<StepInvocationResult> ExecuteAsync(
        SequenceRunRequest request,
        TestSequenceVersion sequence,
        TestStepDefinition step,
        StepInvocation invocation,
        RunCaptureContext captureContext,
        RunObservationCache observationCache,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(sequence);
        ArgumentNullException.ThrowIfNull(step);
        ArgumentNullException.ThrowIfNull(invocation);
        var configuredTimeout = TimeSpan.FromMilliseconds(invocation.TimeoutOverrideMs ?? step.DefaultTimeoutMs);
        var remaining = request.DeadlineUtc - _timeProvider.GetUtcNow();
        var timeout = configuredTimeout <= remaining ? configuredTimeout : remaining;
        if (timeout <= TimeSpan.Zero)
        {
            return Failure(invocation, RunState.TimedOut, ErrorCategory.Timeout, "INVOCATION_DEADLINE", "Invocation 开始前 Run Deadline 已到。 ");
        }

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        var executionTask = ExecuteCoreAsync(
            request,
            sequence,
            step,
            invocation,
            captureContext,
            observationCache,
            timeoutSource.Token);
        try
        {
            return await executionTask.WaitAsync(timeout, cancellationToken);
        }
        catch (TimeoutException)
        {
            timeoutSource.Cancel();
            ObserveLate(executionTask);
            return Failure(invocation, RunState.TimedOut, ErrorCategory.Timeout, "INVOCATION_TIMEOUT", "Invocation 超时；底层 Adapter 若不支持硬取消，其迟到结果将被丢弃。 ");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            timeoutSource.Cancel();
            ObserveLate(executionTask);
            return Failure(invocation, RunState.Stopped, ErrorCategory.Cancellation, "INVOCATION_STOPPED", "Invocation 已停止。 ");
        }
        catch (OperationCanceledException)
        {
            ObserveLate(executionTask);
            return Failure(invocation, RunState.TimedOut, ErrorCategory.Timeout, "INVOCATION_TIMEOUT", "Invocation 超时或 Deadline 已到。 ");
        }
        catch (RunnerExecutionException exception)
        {
            return Failure(invocation, RunState.Errored, exception.Error);
        }
        catch (Exception exception)
        {
            return Failure(invocation, RunState.Errored, ErrorCategory.Inference, "STEP_EXECUTION_ERROR", exception.Message);
        }
    }

    private Task<StepInvocationResult> ExecuteCoreAsync(
        SequenceRunRequest request,
        TestSequenceVersion sequence,
        TestStepDefinition step,
        StepInvocation invocation,
        RunCaptureContext captureContext,
        RunObservationCache observationCache,
        CancellationToken cancellationToken) =>
        step.Kind is TestStepKind.Pose or TestStepKind.Temporal
            ? ExecutePoseAsync(request, sequence, step, invocation, captureContext, cancellationToken)
            : ExecuteRulesAsync(request, sequence, step, invocation, captureContext, observationCache, cancellationToken);

    private async Task<StepInvocationResult> ExecuteRulesAsync(
        SequenceRunRequest request,
        TestSequenceVersion sequence,
        TestStepDefinition step,
        StepInvocation invocation,
        RunCaptureContext captureContext,
        RunObservationCache observationCache,
        CancellationToken cancellationToken)
    {
        var ruleSet = step.RuleSet
            ?? throw RunnerExecutionException.Configuration("RULESET_MISSING", "非 Pose Test Step 缺少 RuleSet。");
        var frame = await _captureCoordinator.CaptureAsync(
            captureContext,
            sequence,
            invocation,
            invocation.CapturePolicy,
            cancellationToken);
        var requiredBindingIds = ruleSet.Rules.Select(rule => rule.ModelBindingId).Distinct();
        var observations = new Dictionary<Guid, ModelObservation>();
        foreach (var bindingId in requiredBindingIds)
        {
            var binding = step.ModelBindings.FirstOrDefault(value => value.ModelBindingId == bindingId)
                ?? throw RunnerExecutionException.Configuration("MODEL_BINDING_MISSING", $"Rule 引用的 Model Binding {bindingId} 不存在。");
            observations[bindingId] = await observationCache.GetOrExecuteAsync(
                frame.CaptureId,
                step.StepId,
                binding.ModelArtifactId,
                binding.AdapterProfileId,
                binding.AdapterId,
                binding.ModelBindingId,
                () => ExecuteModelAsync(request.Project, binding, frame, cancellationToken));
        }

        RuleSetEvaluationV2 evaluation;
        try
        {
            evaluation = RuleSetEvaluatorV2.Evaluate(ruleSet, observations);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidDataException or InvalidOperationException)
        {
            throw new RunnerExecutionException(new StructuredError
            {
                Category = ErrorCategory.RuleEvaluation,
                Code = "RULE_EVALUATION_FAILED",
                Message = exception.Message
            }, exception);
        }

        return new StepInvocationResult
        {
            InvocationId = invocation.InvocationId,
            StepId = invocation.StepId,
            Order = invocation.Order,
            IsRequired = invocation.IsRequired,
            State = RunState.Completed,
            Verdict = evaluation.Verdict,
            Summary = string.Join(
                "; ",
                evaluation.Rules.Select(rule => $"{rule.RuleId:N}={rule.MetricValue}:{rule.Verdict}"))
        };
    }

    private async Task<StepInvocationResult> ExecutePoseAsync(
        SequenceRunRequest request,
        TestSequenceVersion sequence,
        TestStepDefinition step,
        StepInvocation invocation,
        RunCaptureContext captureContext,
        CancellationToken cancellationToken)
    {
        var program = step.PoseProgram
            ?? throw RunnerExecutionException.Configuration("POSE_PROGRAM_MISSING", "Pose Test Step 缺少 Pose Program。");
        var stateMachine = new PoseProgramStateMachine(program);
        var startedTimestamp = _timeProvider.GetTimestamp();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var frame = await _captureCoordinator.CaptureAsync(
                captureContext,
                sequence,
                invocation,
                invocation.CapturePolicy == CapturePolicy.ExternalContextFrame
                    ? CapturePolicy.ExternalContextFrame
                    : CapturePolicy.ContinuousStream,
                cancellationToken);
            var actions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var bindingId in program.Actions.Select(action => action.ModelBindingId).Distinct())
            {
                var binding = step.ModelBindings.FirstOrDefault(value => value.ModelBindingId == bindingId)
                    ?? throw RunnerExecutionException.Configuration("POSE_BINDING_MISSING", $"Pose Action 引用的 Model Binding {bindingId} 不存在。");
                var observation = await ExecuteModelAsync(request.Project, binding, frame, cancellationToken);
                actions.UnionWith(observation.Actions);
            }

            var progress = stateMachine.Process(new PoseFrameSample
            {
                FrameTimestampUtc = frame.HardwareTimestampUtc,
                MonotonicElapsed = _timeProvider.GetElapsedTime(startedTimestamp),
                Actions = actions,
                QualityFlags = frame.QualityFlags
            });
            switch (progress.Status)
            {
                case PoseProgramStatus.Completed:
                    return Success(invocation, BusinessVerdict.Pass, $"Pose Program 已按顺序完成 {progress.CompletedActionIds.Count} 个动作。");
                case PoseProgramStatus.Failed:
                    return new StepInvocationResult
                    {
                        InvocationId = invocation.InvocationId,
                        StepId = invocation.StepId,
                        Order = invocation.Order,
                        IsRequired = invocation.IsRequired,
                        State = RunState.Completed,
                        Verdict = BusinessVerdict.Fail,
                        Error = progress.Error,
                        Summary = progress.Error?.Message ?? "Pose Program 顺序不满足。"
                    };
                case PoseProgramStatus.TimedOut:
                    return Failure(invocation, RunState.TimedOut, progress.Error!);
                case PoseProgramStatus.Error:
                    return Failure(invocation, RunState.Errored, progress.Error!);
                case PoseProgramStatus.InProgress:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }

    private async Task<ModelObservation> ExecuteModelAsync(
        ProjectConfigurationV2 project,
        ModelBindingV2 binding,
        FrameEnvelope frame,
        CancellationToken cancellationToken)
    {
        var artifact = project.ModelArtifacts.FirstOrDefault(value => value.ModelArtifactId == binding.ModelArtifactId)
            ?? throw RunnerExecutionException.Configuration("MODEL_ARTIFACT_MISSING", "Model Binding 引用的 Artifact 不存在。");
        var profile = project.RuntimeProfiles.FirstOrDefault(value => value.RuntimeProfileId == binding.AdapterProfileId)
            ?? throw RunnerExecutionException.Configuration("RUNTIME_PROFILE_MISSING", "Model Binding 引用的 Runtime Profile 不存在。");
        if (!_modelAdapters.TryGet(binding.AdapterId, out var adapter))
        {
            throw RunnerExecutionException.Configuration("MODEL_ADAPTER_MISSING", $"Model Adapter“{binding.AdapterId}”未注册。");
        }

        try
        {
            return await adapter.ExecuteAsync(new ModelExecutionRequest
            {
                Artifact = artifact,
                Binding = binding,
                RuntimeProfile = profile,
                Frame = frame,
                BaseDirectory = _baseDirectory
            }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (RunnerExecutionException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new RunnerExecutionException(new StructuredError
            {
                Category = ErrorCategory.Inference,
                Code = "MODEL_EXECUTION_FAILED",
                Message = exception.Message
            }, exception);
        }
    }

    private static StepInvocationResult Success(
        StepInvocation invocation,
        BusinessVerdict verdict,
        string summary) => new()
        {
            InvocationId = invocation.InvocationId,
            StepId = invocation.StepId,
            Order = invocation.Order,
            IsRequired = invocation.IsRequired,
            State = RunState.Completed,
            Verdict = verdict,
            Summary = summary
        };

    private static StepInvocationResult Failure(
        StepInvocation invocation,
        RunState state,
        ErrorCategory category,
        string code,
        string message) => Failure(invocation, state, new StructuredError
        {
            Category = category,
            Code = code,
            Message = message
        });

    private static StepInvocationResult Failure(
        StepInvocation invocation,
        RunState state,
        StructuredError error) => new()
        {
            InvocationId = invocation.InvocationId,
            StepId = invocation.StepId,
            Order = invocation.Order,
            IsRequired = invocation.IsRequired,
            State = state,
            Verdict = BusinessVerdict.NotEvaluated,
            Error = error,
            Summary = error.Message
        };

    private static void ObserveLate(Task task) => _ = task.ContinueWith(
        static completed => _ = completed.Exception,
        CancellationToken.None,
        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
        TaskScheduler.Default);
}

public sealed class RunObservationCache
{
    private readonly object _sync = new();
    private readonly Dictionary<ModelObservationCacheKey, ModelObservation> _observations = [];

    public async Task<ModelObservation> GetOrExecuteAsync(
        Guid captureId,
        Guid stepId,
        Guid modelArtifactId,
        Guid adapterProfileId,
        string adapterId,
        Guid requestedBindingId,
        Func<Task<ModelObservation>> execute)
    {
        var key = new ModelObservationCacheKey(captureId, stepId, modelArtifactId, adapterProfileId, adapterId);
        lock (_sync)
        {
            if (_observations.TryGetValue(key, out var cached))
            {
                return Rebind(cached, requestedBindingId);
            }
        }

        var observation = await execute();
        lock (_sync)
        {
            _observations[key] = observation;
        }

        return Rebind(observation, requestedBindingId);
    }

    private static ModelObservation Rebind(ModelObservation observation, Guid bindingId)
    {
        if (observation.ModelBindingId == bindingId)
        {
            return observation;
        }

        return observation with
        {
            ModelBindingId = bindingId,
            Counts = observation.Counts.ToDictionary(
                entry => new ModelOutputKey(bindingId, entry.Key.OutputLabelId),
                entry => entry.Value),
            Detections = observation.Detections.Select(detection => detection with
            {
                ModelBindingId = bindingId
            }).ToArray()
        };
    }

    private readonly record struct ModelObservationCacheKey(
        Guid CaptureId,
        Guid StepId,
        Guid ModelArtifactId,
        Guid AdapterProfileId,
        string AdapterId);
}
