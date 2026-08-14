using VisualInspection.Core.Imaging;

namespace VisualInspection.Core.Analysis;

/// <summary>
/// Converts one acquired frame into the observations consumed by inspection rules.
/// A production implementation can wrap ONNX/PT inference; the acceptance build uses
/// a deterministic manifest implementation so the complete execution flow is testable.
/// </summary>
public interface IInspectionProvider
{
    string Name { get; }

    Task<FrameInspectionObservation> AnalyzeAsync(
        ImageFrame frame,
        CancellationToken cancellationToken = default);
}
