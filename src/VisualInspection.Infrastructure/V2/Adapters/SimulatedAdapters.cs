using System.Collections.Concurrent;
using VisualInspection.Core.V2.Configuration;
using VisualInspection.Core.V2.Execution;

namespace VisualInspection.Infrastructure.V2.Adapters;

public sealed class SimulatedModelRuntimeAdapter : IModelRuntimeAdapter
{
    private readonly Func<ModelExecutionRequest, CancellationToken, Task<ModelObservation>> _execute;
    private readonly bool _isReady;

    public SimulatedModelRuntimeAdapter(
        string adapterId,
        TestStepKind taskType,
        Func<ModelExecutionRequest, CancellationToken, Task<ModelObservation>> execute,
        RuntimeProviderKind providerKind = RuntimeProviderKind.Simulator,
        bool supportsHardCancellation = true,
        bool isReady = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(adapterId);
        AdapterId = adapterId;
        TaskType = taskType;
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        ProviderKind = providerKind;
        SupportsHardCancellation = supportsHardCancellation;
        _isReady = isReady;
    }

    public string AdapterId { get; }
    public RuntimeProviderKind ProviderKind { get; }
    public TestStepKind TaskType { get; }
    public bool SupportsHardCancellation { get; }

    public Task<AdapterProbeResult> ProbeAsync(
        ModelArtifact artifact,
        RuntimeProfile runtimeProfile,
        string baseDirectory,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_isReady
            ? AdapterProbeResult.Ready($"模拟 Model Adapter {AdapterId} 已就绪。")
            : AdapterProbeResult.NotReady(NotConfigured("SIM_MODEL_NOT_READY", $"模拟 Model Adapter {AdapterId} 未就绪。")));
    }

    public Task<ModelObservation> ExecuteAsync(
        ModelExecutionRequest request,
        CancellationToken cancellationToken = default) =>
        _execute(request, cancellationToken);

    private static StructuredError NotConfigured(string code, string message) => new()
    {
        Category = ErrorCategory.Configuration,
        Code = code,
        Message = message
    };
}

public sealed class UnconfiguredModelRuntimeAdapter(
    string adapterId,
    TestStepKind taskType) : IModelRuntimeAdapter
{
    public string AdapterId { get; } = adapterId;
    public RuntimeProviderKind ProviderKind => RuntimeProviderKind.Unconfigured;
    public TestStepKind TaskType { get; } = taskType;
    public bool SupportsHardCancellation => false;

    public Task<AdapterProbeResult> ProbeAsync(
        ModelArtifact artifact,
        RuntimeProfile runtimeProfile,
        string baseDirectory,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(AdapterProbeResult.NotReady(Error("MODEL_ADAPTER_UNCONFIGURED", $"Model Adapter“{AdapterId}”尚未接入。")));

    public Task<ModelObservation> ExecuteAsync(
        ModelExecutionRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromException<ModelObservation>(new InvalidOperationException($"Model Adapter“{AdapterId}”未配置，执行被 fail-closed 拒绝。"));

    private static StructuredError Error(string code, string message) => new()
    {
        Category = ErrorCategory.Configuration,
        Code = code,
        Message = message
    };
}

public sealed class SimulatedCameraAdapter : ICameraAdapter
{
    private readonly ConcurrentQueue<FrameEnvelope> _frames = new();
    private readonly bool _isReady;

    public SimulatedCameraAdapter(string adapterId = KnownAdapterIds.SimulatedCamera, bool isReady = true)
    {
        AdapterId = adapterId;
        _isReady = isReady;
    }

    public string AdapterId { get; }
    public bool SupportsHardCancellation => true;
    public bool IsProductionCapable => false;
    public int CaptureCount { get; private set; }

    public void Enqueue(FrameEnvelope frame) => _frames.Enqueue(frame);

    public Task<AdapterProbeResult> ProbeAsync(
        InputSourceDeploymentBinding binding,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_isReady
            ? AdapterProbeResult.Ready("模拟 Camera Adapter 已就绪。")
            : AdapterProbeResult.NotReady(new StructuredError
            {
                Category = ErrorCategory.Capture,
                Code = "SIM_CAMERA_NOT_READY",
                Message = "模拟 Camera Adapter 未就绪。"
            }));

    public Task<FrameEnvelope> CaptureAsync(
        CaptureRequest request,
        TriggerEvent trigger,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_isReady)
        {
            throw new InvalidOperationException("模拟 Camera Adapter 未就绪。");
        }

        if (!_frames.TryDequeue(out var frame))
        {
            throw new EndOfStreamException("模拟 Camera Adapter 没有待采集 Frame。");
        }

        CaptureCount++;
        return Task.FromResult(frame with
        {
            CaptureId = request.CaptureId,
            TriggerEventId = request.TriggerEventId,
            ProductId = request.ProductId,
            StationId = trigger.StationId
        });
    }
}

public sealed class UnconfiguredCameraAdapter(
    string adapterId = KnownAdapterIds.UnconfiguredCamera) : ICameraAdapter
{
    public string AdapterId { get; } = adapterId;
    public bool SupportsHardCancellation => false;
    public bool IsProductionCapable => false;

    public Task<AdapterProbeResult> ProbeAsync(
        InputSourceDeploymentBinding binding,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(AdapterProbeResult.NotReady(new StructuredError
        {
            Category = ErrorCategory.Configuration,
            Code = "CAMERA_UNCONFIGURED",
            Message = "真实 Camera Adapter 尚未配置。"
        }));

    public Task<FrameEnvelope> CaptureAsync(
        CaptureRequest request,
        TriggerEvent trigger,
        CancellationToken cancellationToken = default) =>
        Task.FromException<FrameEnvelope>(new InvalidOperationException("Camera Adapter 未配置，采集被 fail-closed 拒绝。"));
}

public sealed class SimulatedTriggerAdapter(
    string adapterId = "trigger-simulator",
    bool isReady = true) : ITriggerAdapter
{
    public string AdapterId { get; } = adapterId;
    public bool IsProductionCapable => false;

    public Task<AdapterProbeResult> ProbeAsync(
        TriggerSignalDeploymentBinding binding,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(isReady
            ? AdapterProbeResult.Ready("模拟 Trigger Adapter 已就绪。")
            : AdapterProbeResult.NotReady(new StructuredError
            {
                Category = ErrorCategory.Trigger,
                Code = "SIM_TRIGGER_NOT_READY",
                Message = "模拟 Trigger Adapter 未就绪。"
            }));
}

public sealed class UnconfiguredTriggerAdapter(string adapterId = "trigger-unconfigured") : ITriggerAdapter
{
    public string AdapterId { get; } = adapterId;
    public bool IsProductionCapable => false;

    public Task<AdapterProbeResult> ProbeAsync(
        TriggerSignalDeploymentBinding binding,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(AdapterProbeResult.NotReady(new StructuredError
        {
            Category = ErrorCategory.Configuration,
            Code = "TRIGGER_UNCONFIGURED",
            Message = "真实 Trigger Adapter 尚未配置。"
        }));
}

public sealed class SimulatedLineResultAdapter : ILineResultAdapter
{
    private readonly LineResultHandshakeStateMachine _stateMachine;
    private readonly TimeProvider _timeProvider;
    private readonly bool _isReady;

    public SimulatedLineResultAdapter(
        TimeSpan? ackTimeout = null,
        TimeProvider? timeProvider = null,
        bool isReady = true,
        string adapterId = KnownAdapterIds.SimulatedLineResult)
    {
        _stateMachine = new LineResultHandshakeStateMachine(ackTimeout);
        _timeProvider = timeProvider ?? TimeProvider.System;
        _isReady = isReady;
        AdapterId = adapterId;
    }

    public string AdapterId { get; }
    public bool IsProductionCapable => false;

    public Task<AdapterProbeResult> ProbeAsync(
        LineResultDeploymentBinding binding,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_isReady
            ? AdapterProbeResult.Ready("模拟 Line Result Adapter 已连接。")
            : AdapterProbeResult.NotReady(new StructuredError
            {
                Category = ErrorCategory.LineCommunication,
                Code = "SIM_LINE_NOT_READY",
                Message = "模拟 Line Result Adapter 未连接。"
            }));

    public Task<LineResultSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_stateMachine.Snapshot);

    public Task SetReadyAsync(bool ready, CancellationToken cancellationToken = default) =>
        Complete(_stateMachine.SetReady(ready));

    public Task BeginRunAsync(Guid runId, CancellationToken cancellationToken = default) =>
        Complete(_stateMachine.BeginRun(runId));

    public Task LatchResultAsync(LineResultPayload result, CancellationToken cancellationToken = default) =>
        Complete(_stateMachine.Latch(result, _timeProvider.GetUtcNow()));

    public Task AcknowledgeAsync(long resultSequenceNumber, CancellationToken cancellationToken = default) =>
        Complete(_stateMachine.Acknowledge(resultSequenceNumber));

    public Task FaultAsync(StructuredError error, CancellationToken cancellationToken = default) =>
        Complete(_stateMachine.Fault());

    public Task DisconnectAsync(CancellationToken cancellationToken = default) =>
        Complete(_stateMachine.Disconnect());

    public LineResultSnapshot Tick() => _stateMachine.Tick(_timeProvider.GetUtcNow());

    public LineResultSnapshot Restart() => _stateMachine.Restart();

    private static Task Complete(LineResultSnapshot _) => Task.CompletedTask;
}

public sealed class UnconfiguredLineResultAdapter(
    string adapterId = KnownAdapterIds.UnconfiguredLineResult) : ILineResultAdapter
{
    private readonly LineResultSnapshot _snapshot = new();

    public string AdapterId { get; } = adapterId;
    public bool IsProductionCapable => false;

    public Task<AdapterProbeResult> ProbeAsync(
        LineResultDeploymentBinding binding,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(AdapterProbeResult.NotReady(new StructuredError
        {
            Category = ErrorCategory.Configuration,
            Code = "LINE_UNCONFIGURED",
            Message = "真实 Line Result Adapter 尚未配置；系统保持 NotReady。"
        }));

    public Task<LineResultSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default) => Task.FromResult(_snapshot);
    public Task SetReadyAsync(bool ready, CancellationToken cancellationToken = default) => Reject();
    public Task BeginRunAsync(Guid runId, CancellationToken cancellationToken = default) => Reject();
    public Task LatchResultAsync(LineResultPayload result, CancellationToken cancellationToken = default) => Reject();
    public Task AcknowledgeAsync(long resultSequenceNumber, CancellationToken cancellationToken = default) => Reject();
    public Task FaultAsync(StructuredError error, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task DisconnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    private static Task Reject() => Task.FromException(new InvalidOperationException("Line Result Adapter 未配置，系统保持 NotReady。"));
}
