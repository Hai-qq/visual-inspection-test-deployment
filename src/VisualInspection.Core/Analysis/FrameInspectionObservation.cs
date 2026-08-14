namespace VisualInspection.Core.Analysis;

public sealed record FrameInspectionObservation
{
    public required IReadOnlyDictionary<Guid, int> TargetCounts { get; init; }
    public IReadOnlyList<TargetDetection>? Detections { get; init; }
    public IReadOnlySet<string> Actions { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public string ProviderDetails { get; init; } = string.Empty;

    public int GetTargetCount(Guid targetId) =>
        TargetCounts.TryGetValue(targetId, out var count) ? count : 0;

    public bool HasAction(string actionCondition) =>
        Actions.Contains(actionCondition);
}
