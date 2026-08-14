using VisualInspection.Core.Analysis;
using VisualInspection.Core.Domain;
using VisualInspection.Core.Imaging;

namespace VisualInspection.Core.Execution;

public enum TestRunUpdateKind
{
    RunStarted,
    ItemStarted,
    FrameAcquired,
    FrameAnalyzed,
    ItemCompleted,
    RunCompleted,
    RunStopped,
    RunError
}

public sealed record TestRunUpdate
{
    public required TestRunUpdateKind Kind { get; init; }
    public Guid? ItemId { get; init; }
    public int? ItemOrder { get; init; }
    public string ItemName { get; init; } = string.Empty;
    public InspectionVerdict? Verdict { get; init; }
    public string Message { get; init; } = string.Empty;
    public ImageFrame? Frame { get; init; }
    public IReadOnlyList<TargetDetection>? Detections { get; init; }
}

public sealed record TestItemRunResult
{
    public required Guid ItemId { get; init; }
    public required int ItemOrder { get; init; }
    public required string ItemName { get; init; }
    public required bool IsRequired { get; init; }
    public required InspectionVerdict Verdict { get; init; }
    public required string Measured { get; init; }
    public string? ErrorCode { get; init; }
}

public sealed record TestRunResult
{
    public required Guid RunId { get; init; }
    public required DateTimeOffset StartedAtUtc { get; init; }
    public required DateTimeOffset CompletedAtUtc { get; init; }
    public required InspectionVerdict Verdict { get; init; }
    public required IReadOnlyList<TestItemRunResult> Items { get; init; }
    public bool WasStopped { get; init; }
    public string? ErrorCode { get; init; }
    public string Summary { get; init; } = string.Empty;
}
