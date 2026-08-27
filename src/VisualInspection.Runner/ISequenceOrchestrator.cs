using VisualInspection.Core.V2.Configuration;
using VisualInspection.Core.V2.Execution;

namespace VisualInspection.Runner;

public interface ISequenceOrchestrator : IAsyncDisposable
{
    Task<SequenceReadinessResult> PrepareAsync(
        ProjectConfigurationV2 project,
        Guid sequenceId,
        string sequenceVersion,
        Guid deploymentBindingId,
        RuntimeEnvironmentMode environmentMode,
        CancellationToken cancellationToken = default);

    Task<SequenceRunResult> EnqueueAsync(
        SequenceRunRequest request,
        CancellationToken cancellationToken = default);

    Task StopAsync(Guid runId, CancellationToken cancellationToken = default);
}

public sealed record SequenceOrchestratorOptions
{
    public int QueueCapacity { get; init; } = 8;
    public int MaxInFlightRuns { get; init; } = 1;
    public QueueOverflowPolicy OverflowPolicy { get; init; } = QueueOverflowPolicy.RejectNewest;
    public int IdempotencyRetentionCapacity { get; init; } = 1024;
    public string BaseDirectory { get; init; } = Directory.GetCurrentDirectory();
}
