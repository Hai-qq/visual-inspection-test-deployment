using VisualInspection.Core.Domain;

namespace VisualInspection.Core.Execution;

public sealed record ExecutionAuditEntry
{
    public required DateTimeOffset TimestampUtc { get; init; }
    public required string Level { get; init; }
    public required string Event { get; init; }
    public required string Message { get; init; }
    public Guid? RunId { get; init; }
    public string? ItemName { get; init; }
    public InspectionVerdict? Verdict { get; init; }
}
