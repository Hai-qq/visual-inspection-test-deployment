using VisualInspection.Core.V2.Configuration;
using VisualInspection.Core.V2.Execution;

namespace VisualInspection.Runner;

public sealed class CaptureCoordinator
{
    private readonly ICameraAdapterRegistry _cameraAdapters;
    private readonly FrameCorrelationService _correlation;
    private readonly TimeProvider _timeProvider;

    public CaptureCoordinator(
        ICameraAdapterRegistry cameraAdapters,
        FrameCorrelationService? correlation = null,
        TimeProvider? timeProvider = null)
    {
        _cameraAdapters = cameraAdapters ?? throw new ArgumentNullException(nameof(cameraAdapters));
        _correlation = correlation ?? new FrameCorrelationService();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public RunCaptureContext CreateContext(SequenceRunRequest request, DeploymentBinding deployment) =>
        new(request, deployment);

    public Task<FrameEnvelope> CaptureAsync(
        RunCaptureContext context,
        TestSequenceVersion sequence,
        StepInvocation invocation,
        CapturePolicy capturePolicy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var sourceBindingId = invocation.SourceBindingId ?? sequence.DefaultSourceBindingId;
        return capturePolicy switch
        {
            CapturePolicy.CaptureOncePerProduct => context.GetOrAddShared(
                sourceBindingId,
                () => CaptureCoreAsync(context, sourceBindingId, CaptureMode.SingleFrame, cancellationToken)),
            CapturePolicy.ExternalContextFrame => context.GetOrAddExternal(
                () => CorrelateExternal(context, sourceBindingId)),
            CapturePolicy.CapturePerInvocation =>
                CaptureCoreAsync(context, sourceBindingId, CaptureMode.SingleFrame, cancellationToken),
            CapturePolicy.ContinuousStream =>
                CaptureCoreAsync(context, sourceBindingId, CaptureMode.Continuous, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(capturePolicy))
        };
    }

    public void CancelRun(Guid runId) => _correlation.CancelRun(runId);

    private async Task<FrameEnvelope> CaptureCoreAsync(
        RunCaptureContext context,
        Guid sourceBindingId,
        CaptureMode captureMode,
        CancellationToken cancellationToken)
    {
        var source = context.Request.Project.InputSourceDefinitions.FirstOrDefault(value =>
            value.SourceBindingId == sourceBindingId)
            ?? throw RunnerExecutionException.Configuration("CAPTURE_SOURCE_UNKNOWN", "Capture Source Binding 不存在。");
        var binding = context.Deployment.InputSourceBindings.FirstOrDefault(value =>
            value.SourceBindingId == sourceBindingId)
            ?? throw RunnerExecutionException.Configuration("CAPTURE_DEPLOYMENT_UNKNOWN", "Capture Source 没有 Deployment Binding。");
        if (!_cameraAdapters.TryGet(binding.CameraAdapterId, out var camera))
        {
            throw RunnerExecutionException.Configuration("CAMERA_ADAPTER_MISSING", $"Camera Adapter“{binding.CameraAdapterId}”未注册。");
        }

        var now = _timeProvider.GetUtcNow();
        var request = new CaptureRequest
        {
            RunId = context.Request.RunId,
            TriggerEventId = context.Request.Trigger.EventId,
            ProductId = context.Request.Trigger.ProductId,
            SourceBindingId = sourceBindingId,
            RequestedAtUtc = now,
            DeadlineUtc = context.Request.DeadlineUtc,
            CaptureMode = captureMode
        };
        _correlation.Register(request, context.Deployment.StationId, source.MaximumFrameAgeMs);
        FrameEnvelope frame;
        try
        {
            frame = await camera.CaptureAsync(request, context.Request.Trigger, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _correlation.AbandonCapture(request.CaptureId);
            throw;
        }
        catch (Exception exception)
        {
            _correlation.AbandonCapture(request.CaptureId);
            throw new RunnerExecutionException(new StructuredError
            {
                Category = ErrorCategory.Capture,
                Code = "CAPTURE_FAILED",
                Message = exception.Message,
                IsTransient = true
            }, exception);
        }

        return Correlate(frame);
    }

    private Task<FrameEnvelope> CorrelateExternal(RunCaptureContext context, Guid sourceBindingId)
    {
        var frame = context.Request.ExternalContextFrame
            ?? throw RunnerExecutionException.Correlation("EXTERNAL_FRAME_MISSING", "Invocation 要求 ExternalContextFrame，但 Run 未提供。");
        var source = context.Request.Project.InputSourceDefinitions.FirstOrDefault(value =>
            value.SourceBindingId == sourceBindingId)
            ?? throw RunnerExecutionException.Configuration("CAPTURE_SOURCE_UNKNOWN", "External Frame Source Binding 不存在。");
        var request = new CaptureRequest
        {
            CaptureId = frame.CaptureId,
            RunId = context.Request.RunId,
            TriggerEventId = context.Request.Trigger.EventId,
            ProductId = context.Request.Trigger.ProductId,
            SourceBindingId = sourceBindingId,
            RequestedAtUtc = context.Request.Trigger.ReceivedAtUtc,
            DeadlineUtc = context.Request.DeadlineUtc,
            CaptureMode = CaptureMode.SingleFrame
        };
        _correlation.Register(request, context.Deployment.StationId, source.MaximumFrameAgeMs);
        return Task.FromResult(Correlate(frame));
    }

    private FrameEnvelope Correlate(FrameEnvelope frame)
    {
        var result = _correlation.Correlate(frame, _timeProvider.GetUtcNow());
        if (!result.IsCorrelated)
        {
            throw new RunnerExecutionException(result.Error!);
        }

        return result.Frame!;
    }
}

public sealed class RunCaptureContext
{
    private readonly object _sync = new();
    private readonly Dictionary<Guid, Task<FrameEnvelope>> _sharedFrames = [];
    private Task<FrameEnvelope>? _externalFrame;

    internal RunCaptureContext(SequenceRunRequest request, DeploymentBinding deployment)
    {
        Request = request;
        Deployment = deployment;
    }

    public SequenceRunRequest Request { get; }
    public DeploymentBinding Deployment { get; }

    internal Task<FrameEnvelope> GetOrAddShared(Guid sourceBindingId, Func<Task<FrameEnvelope>> factory)
    {
        lock (_sync)
        {
            if (!_sharedFrames.TryGetValue(sourceBindingId, out var frame))
            {
                frame = factory();
                _sharedFrames[sourceBindingId] = frame;
            }

            return frame;
        }
    }

    internal Task<FrameEnvelope> GetOrAddExternal(Func<Task<FrameEnvelope>> factory)
    {
        lock (_sync)
        {
            return _externalFrame ??= factory();
        }
    }
}

public sealed class RunnerExecutionException : Exception
{
    public RunnerExecutionException(StructuredError error, Exception? innerException = null)
        : base(error.Message, innerException)
    {
        Error = error;
    }

    public StructuredError Error { get; }

    public static RunnerExecutionException Configuration(string code, string message) => new(new StructuredError
    {
        Category = ErrorCategory.Configuration,
        Code = code,
        Message = message
    });

    public static RunnerExecutionException Correlation(string code, string message) => new(new StructuredError
    {
        Category = ErrorCategory.FrameCorrelation,
        Code = code,
        Message = message
    });
}
