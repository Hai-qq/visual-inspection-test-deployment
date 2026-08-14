using VisualInspection.Core.Configuration;
using VisualInspection.Core.Imaging;

namespace VisualInspection.Core.Analysis;

/// <summary>
/// Contract for a real PT/ONNX runtime. The acceptance build deliberately does not
/// claim that a runtime is connected; adapters must report readiness before use.
/// </summary>
public interface IModelInferenceRuntime
{
    string RuntimeName { get; }
    bool IsReady { get; }
    string ReadinessDetails { get; }

    Task<FrameInspectionObservation> InferAsync(
        ImageFrame frame,
        IReadOnlyList<ModelDefinition> models,
        CancellationToken cancellationToken = default);
}
