using Microsoft.ML.OnnxRuntime;

namespace VisualInspection.Infrastructure.Analysis;

public static class OnnxModelContractInspector
{
    public static OnnxModelContract Inspect(string modelPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);
        var resolvedPath = Path.GetFullPath(modelPath);
        if (!File.Exists(resolvedPath))
        {
            throw new FileNotFoundException("ONNX 模型文件不存在。", resolvedPath);
        }

        using var session = new InferenceSession(resolvedPath, CreateSessionOptions());
        return new OnnxModelContract
        {
            ProducerName = session.ModelMetadata.ProducerName ?? string.Empty,
            GraphName = session.ModelMetadata.GraphName ?? string.Empty,
            CustomMetadata = new Dictionary<string, string>(
                session.ModelMetadata.CustomMetadataMap,
                StringComparer.OrdinalIgnoreCase),
            Inputs = session.InputMetadata.Select(pair => ToTensorContract(pair.Key, pair.Value)).ToArray(),
            Outputs = session.OutputMetadata.Select(pair => ToTensorContract(pair.Key, pair.Value)).ToArray()
        };
    }

    internal static SessionOptions CreateSessionOptions()
    {
        var options = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL
        };
        options.AppendExecutionProvider_CPU();
        return options;
    }

    private static OnnxTensorContract ToTensorContract(string name, NodeMetadata metadata) =>
        new()
        {
            Name = name,
            ElementType = metadata.ElementDataType.ToString(),
            Dimensions = metadata.Dimensions.ToArray()
        };
}

public sealed record OnnxModelContract
{
    public string ProducerName { get; init; } = string.Empty;
    public string GraphName { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, string> CustomMetadata { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<OnnxTensorContract> Inputs { get; init; } = [];
    public IReadOnlyList<OnnxTensorContract> Outputs { get; init; } = [];
}

public sealed record OnnxTensorContract
{
    public string Name { get; init; } = string.Empty;
    public string ElementType { get; init; } = string.Empty;
    public IReadOnlyList<int> Dimensions { get; init; } = [];

    public override string ToString() =>
        $"{Name}: {ElementType}[{string.Join(",", Dimensions)}]";
}
