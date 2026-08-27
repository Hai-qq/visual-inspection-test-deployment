using VisualInspection.Core.V2.Configuration;

namespace VisualInspection.Core.V2.Execution;

public sealed record AdapterProbeResult
{
    public required bool IsReady { get; init; }
    public string Status { get; init; } = string.Empty;
    public StructuredError? Error { get; init; }

    public static AdapterProbeResult Ready(string status) => new() { IsReady = true, Status = status };

    public static AdapterProbeResult NotReady(StructuredError error) => new()
    {
        IsReady = false,
        Status = error.Message,
        Error = error
    };
}

public sealed record ModelExecutionRequest
{
    public required ModelArtifact Artifact { get; init; }
    public required ModelBindingV2 Binding { get; init; }
    public required RuntimeProfile RuntimeProfile { get; init; }
    public required FrameEnvelope Frame { get; init; }
    public string BaseDirectory { get; init; } = string.Empty;
}

public interface IModelRuntimeAdapter
{
    string AdapterId { get; }
    RuntimeProviderKind ProviderKind { get; }
    TestStepKind TaskType { get; }
    bool SupportsHardCancellation { get; }

    Task<AdapterProbeResult> ProbeAsync(
        ModelArtifact artifact,
        RuntimeProfile runtimeProfile,
        string baseDirectory,
        CancellationToken cancellationToken = default);

    Task<ModelObservation> ExecuteAsync(
        ModelExecutionRequest request,
        CancellationToken cancellationToken = default);
}

public interface IModelRuntimeAdapterRegistry
{
    bool TryGet(string adapterId, out IModelRuntimeAdapter adapter);
    IReadOnlyCollection<IModelRuntimeAdapter> All { get; }
}

public interface ICameraAdapter
{
    string AdapterId { get; }
    bool SupportsHardCancellation { get; }
    bool IsProductionCapable { get; }

    Task<AdapterProbeResult> ProbeAsync(
        InputSourceDeploymentBinding binding,
        CancellationToken cancellationToken = default);

    Task<FrameEnvelope> CaptureAsync(
        CaptureRequest request,
        TriggerEvent trigger,
        CancellationToken cancellationToken = default);
}

public interface ICameraAdapterRegistry
{
    bool TryGet(string adapterId, out ICameraAdapter adapter);
    IReadOnlyCollection<ICameraAdapter> All { get; }
}

public interface ITriggerAdapter
{
    string AdapterId { get; }
    bool IsProductionCapable { get; }
    Task<AdapterProbeResult> ProbeAsync(
        TriggerSignalDeploymentBinding binding,
        CancellationToken cancellationToken = default);
}

public interface ITriggerAdapterRegistry
{
    bool TryGet(string adapterId, out ITriggerAdapter adapter);
    IReadOnlyCollection<ITriggerAdapter> All { get; }
}

public enum LineHandshakeState
{
    NotReady,
    Ready,
    Busy,
    ResultLatched,
    Faulted
}

public sealed record LineResultPayload
{
    public required Guid RunId { get; init; }
    public required long ResultSequenceNumber { get; init; }
    public required RunState RunState { get; init; }
    public required BusinessVerdict Verdict { get; init; }
    public StructuredError? Error { get; init; }
}

public sealed record LineResultSnapshot
{
    public LineHandshakeState State { get; init; } = LineHandshakeState.NotReady;
    public bool Ready { get; init; }
    public bool Busy { get; init; }
    public bool ResultValid { get; init; }
    public bool Pass { get; init; }
    public bool Fail { get; init; }
    public bool Error { get; init; }
    public bool Heartbeat { get; init; }
    public bool Ack { get; init; }
    public long ResultSequenceNumber { get; init; }
    public Guid? ActiveRunId { get; init; }
    public DateTimeOffset? AckDeadlineUtc { get; init; }
}

public interface ILineResultAdapter
{
    string AdapterId { get; }
    bool IsProductionCapable { get; }

    Task<AdapterProbeResult> ProbeAsync(
        LineResultDeploymentBinding binding,
        CancellationToken cancellationToken = default);

    Task<LineResultSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);
    Task SetReadyAsync(bool ready, CancellationToken cancellationToken = default);
    Task BeginRunAsync(Guid runId, CancellationToken cancellationToken = default);
    Task LatchResultAsync(LineResultPayload result, CancellationToken cancellationToken = default);
    Task AcknowledgeAsync(long resultSequenceNumber, CancellationToken cancellationToken = default);
    Task FaultAsync(StructuredError error, CancellationToken cancellationToken = default);
    Task DisconnectAsync(CancellationToken cancellationToken = default);
}

public interface ILineResultAdapterRegistry
{
    bool TryGet(string adapterId, out ILineResultAdapter adapter);
    IReadOnlyCollection<ILineResultAdapter> All { get; }
}

public sealed record SequenceReadinessResult
{
    public required bool IsReady { get; init; }
    public required IReadOnlyList<V2ValidationIssue> Issues { get; init; }
    public required IReadOnlyList<ProviderRecord> Providers { get; init; }
}

public interface ISequenceReadinessGate
{
    Task<SequenceReadinessResult> EvaluateAsync(
        ProjectConfigurationV2 project,
        TestSequenceVersion sequence,
        DeploymentBinding deployment,
        RuntimeEnvironmentMode environmentMode,
        CancellationToken cancellationToken = default);
}
