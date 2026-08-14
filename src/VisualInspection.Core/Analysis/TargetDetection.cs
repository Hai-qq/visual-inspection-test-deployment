namespace VisualInspection.Core.Analysis;

/// <summary>
/// One model detection normalized to absolute pixel coordinates in the source frame.
/// </summary>
public sealed record TargetDetection
{
    public required Guid TargetId { get; init; }
    public required Guid ModelBindingId { get; init; }
    public required double X1 { get; init; }
    public required double Y1 { get; init; }
    public required double X2 { get; init; }
    public required double Y2 { get; init; }
    public required double Confidence { get; init; }

    public double CenterX => (X1 + X2) / 2;
    public double CenterY => (Y1 + Y2) / 2;
}
