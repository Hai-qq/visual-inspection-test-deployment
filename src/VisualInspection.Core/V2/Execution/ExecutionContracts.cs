using VisualInspection.Core.Imaging;
using VisualInspection.Core.V2.Configuration;

namespace VisualInspection.Core.V2.Execution;

public enum RunState
{
    Pending,
    Queued,
    Running,
    Completed,
    Errored,
    Stopped,
    TimedOut
}

public enum BusinessVerdict
{
    Pass,
    Fail,
    NotEvaluated
}

public enum ErrorCategory
{
    Trigger,
    Capture,
    FrameCorrelation,
    ModelLoad,
    Inference,
    Postprocess,
    RuleEvaluation,
    Timeout,
    Cancellation,
    LineCommunication,
    Configuration,
    Resource
}

[Flags]
public enum FrameQualityFlags
{
    None = 0,
    Incomplete = 1,
    Corrupt = 2,
    TimestampUncertain = 4,
    DroppedFramesDetected = 8
}

public enum RuntimeProviderKind
{
    OnnxYoloEndToEnd,
    DeterministicManifest,
    Simulator,
    Unconfigured
}

public sealed record StructuredError
{
    public required ErrorCategory Category { get; init; }
    public required string Code { get; init; }
    public required string Message { get; init; }
    public bool IsTransient { get; init; }
}

public sealed record TriggerEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public string SignalTag { get; init; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; init; }
    public DateTimeOffset ReceivedAtUtc { get; init; }
    public string ProductId { get; init; } = string.Empty;
    public string StationId { get; init; } = string.Empty;
    public string? BatchId { get; init; }
    public long SequenceNumber { get; init; }
    public string IdempotencyKey { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, string> Metadata { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

public sealed record CaptureRequest
{
    public Guid CaptureId { get; init; } = Guid.NewGuid();
    public Guid RunId { get; init; }
    public Guid TriggerEventId { get; init; }
    public string ProductId { get; init; } = string.Empty;
    public Guid SourceBindingId { get; init; }
    public DateTimeOffset RequestedAtUtc { get; init; }
    public DateTimeOffset DeadlineUtc { get; init; }
    public CaptureMode CaptureMode { get; init; } = CaptureMode.SingleFrame;
}

public sealed record FrameEnvelope
{
    public required ImageFrame ImageFrame { get; init; }
    public required Guid CaptureId { get; init; }
    public required Guid TriggerEventId { get; init; }
    public required string ProductId { get; init; }
    public required string StationId { get; init; }
    public required DateTimeOffset HardwareTimestampUtc { get; init; }
    public required DateTimeOffset ReceivedAtUtc { get; init; }
    public required long FrameCounter { get; init; }
    public FrameQualityFlags QualityFlags { get; init; }
}

public readonly record struct ModelOutputKey(Guid ModelBindingId, int OutputLabelId);

public sealed record ModelDetectionV2
{
    public required Guid ModelBindingId { get; init; }
    public required int OutputLabelId { get; init; }
    public required double X1 { get; init; }
    public required double Y1 { get; init; }
    public required double X2 { get; init; }
    public required double Y2 { get; init; }
    public required double Confidence { get; init; }
    public double CenterX => (X1 + X2) / 2;
    public double CenterY => (Y1 + Y2) / 2;
}

public sealed record ModelObservation
{
    public required Guid CaptureId { get; init; }
    public required Guid ModelBindingId { get; init; }
    public required DateTimeOffset ObservedAtUtc { get; init; }
    public IReadOnlyDictionary<ModelOutputKey, int> Counts { get; init; } =
        new Dictionary<ModelOutputKey, int>();
    public IReadOnlyList<ModelDetectionV2> Detections { get; init; } = [];
    public int FrameWidth { get; init; }
    public int FrameHeight { get; init; }
    public IReadOnlySet<string> Actions { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public string ProviderDetails { get; init; } = string.Empty;

    public int GetCount(Guid modelBindingId, int outputLabelId) =>
        Counts.TryGetValue(new ModelOutputKey(modelBindingId, outputLabelId), out var count) ? count : 0;
}

public sealed record StepInvocationResult
{
    public required Guid InvocationId { get; init; }
    public required Guid StepId { get; init; }
    public required int Order { get; init; }
    public required bool IsRequired { get; init; }
    public required RunState State { get; init; }
    public required BusinessVerdict Verdict { get; init; }
    public StructuredError? Error { get; init; }
    public string Summary { get; init; } = string.Empty;
}

public sealed record TestRecord
{
    public required Guid RunId { get; init; }
    public required RuntimeEnvironmentMode EnvironmentMode { get; init; }
    public required IReadOnlyList<ProviderRecord> Providers { get; init; }
    public required Guid SequenceId { get; init; }
    public required string SequenceVersion { get; init; }
    public required long TriggerSequenceNumber { get; init; }
    public required string IdempotencyKey { get; init; }
    public required DateTimeOffset StartedAtUtc { get; init; }
    public required DateTimeOffset CompletedAtUtc { get; init; }
}

public sealed record ProviderRecord(
    string AdapterId,
    RuntimeProviderKind ProviderKind,
    bool SupportsHardCancellation);

public sealed record SequenceRunResult
{
    public required Guid RunId { get; init; }
    public required RunState State { get; init; }
    public required BusinessVerdict Verdict { get; init; }
    public required DateTimeOffset StartedAtUtc { get; init; }
    public required DateTimeOffset CompletedAtUtc { get; init; }
    public required IReadOnlyList<StepInvocationResult> Invocations { get; init; }
    public StructuredError? Error { get; init; }
    public required TestRecord TestRecord { get; init; }
}

public sealed record SequenceRunRequest
{
    public Guid RunId { get; init; } = Guid.NewGuid();
    public required ProjectConfigurationV2 Project { get; init; }
    public required Guid SequenceId { get; init; }
    public required string SequenceVersion { get; init; }
    public required Guid DeploymentBindingId { get; init; }
    public required TriggerEvent Trigger { get; init; }
    public required RuntimeEnvironmentMode EnvironmentMode { get; init; }
    public required DateTimeOffset DeadlineUtc { get; init; }
    public FrameEnvelope? ExternalContextFrame { get; init; }
}
