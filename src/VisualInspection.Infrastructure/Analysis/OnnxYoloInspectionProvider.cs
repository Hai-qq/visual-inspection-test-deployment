using System.Diagnostics;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using VisualInspection.Core.Analysis;
using VisualInspection.Core.Configuration;
using VisualInspection.Core.Imaging;

namespace VisualInspection.Infrastructure.Analysis;

/// <summary>
/// Runs static-shape Ultralytics YOLO detection models exported with end-to-end
/// output [1,N,6] = x1,y1,x2,y2,confidence,classId on the CPU provider.
/// </summary>
public sealed class OnnxYoloInspectionProvider : IInspectionProvider, IDisposable
{
    private readonly IReadOnlyList<ModelRuntime> _models;
    private bool _disposed;

    private OnnxYoloInspectionProvider(IReadOnlyList<ModelRuntime> models)
    {
        _models = models;
    }

    public string Name => $"ONNX Runtime CPU · YOLO 端到端检测 · {_models.Count} 个模型";

    public static OnnxRuntimeProbe Probe(
        ProjectConfiguration project,
        TestSequenceDefinition sequence,
        string baseDirectory)
    {
        try
        {
            var plans = BuildPlans(project, sequence, baseDirectory);
            foreach (var plan in plans)
            {
                var contract = OnnxModelContractInspector.Inspect(plan.ModelPath);
                ValidateContract(plan.Model, contract);
            }

            return new OnnxRuntimeProbe(
                true,
                $"真实 ONNX Runtime 已就绪 · YOLO 端到端检测 · {plans.Count} 个模型");
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or InvalidDataException or
                                           InvalidOperationException or NotSupportedException or OnnxRuntimeException)
        {
            return new OnnxRuntimeProbe(false, $"真实 ONNX Runtime 不可用：{exception.Message}");
        }
    }

    public static OnnxYoloInspectionProvider Create(
        ProjectConfiguration project,
        TestSequenceDefinition sequence,
        string baseDirectory)
    {
        var plans = BuildPlans(project, sequence, baseDirectory);
        var runtimes = new List<ModelRuntime>();
        try
        {
            foreach (var plan in plans)
            {
                runtimes.Add(new ModelRuntime(plan, new SkiaFramePreprocessor()));
            }

            return new OnnxYoloInspectionProvider(runtimes);
        }
        catch
        {
            foreach (var runtime in runtimes)
            {
                runtime.Dispose();
            }

            throw;
        }
    }

    public async Task<FrameInspectionObservation> AnalyzeAsync(
        ImageFrame frame,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(frame);
        cancellationToken.ThrowIfCancellationRequested();

        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var timer = Stopwatch.StartNew();
            var detections = new List<TargetDetection>();
            foreach (var model in _models)
            {
                cancellationToken.ThrowIfCancellationRequested();
                detections.AddRange(model.Infer(frame));
            }

            timer.Stop();
            var counts = detections
                .GroupBy(detection => detection.TargetId)
                .ToDictionary(group => group.Key, group => group.Count());
            return new FrameInspectionObservation
            {
                TargetCounts = counts,
                Detections = detections,
                ProviderDetails = $"真实 ONNX Runtime CPU · {_models.Count} 个模型 · {timer.ElapsedMilliseconds} 毫秒"
            };
        }, cancellationToken);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (var model in _models)
        {
            model.Dispose();
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private static IReadOnlyList<ModelPlan> BuildPlans(
        ProjectConfiguration project,
        TestSequenceDefinition sequence,
        string baseDirectory)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(sequence);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);

        var enabledItems = sequence.Items.Where(item => item.Enabled).ToArray();
        if (enabledItems.Length == 0)
        {
            throw new InvalidOperationException("当前测试序列没有已启用的测试项。");
        }

        if (enabledItems.Any(item => item.Type == TestItemType.PoseSequence))
        {
            throw new NotSupportedException("真实 ONNX 运行时当前仅支持目标检测；启用的姿态时序项尚未接入真实关键点输出。");
        }

        var bindingLookup = project.Targets
            .SelectMany(target => target.ModelBindings.Select(binding => new BoundTarget(target, binding)))
            .ToDictionary(pair => pair.Binding.Id);
        var rules = enabledItems.SelectMany(item => item.Rules).ToArray();
        if (rules.Length == 0)
        {
            throw new InvalidOperationException("当前测试序列没有可执行的目标检测规则。");
        }

        var plans = new List<ModelPlan>();
        foreach (var modelGroup in rules.GroupBy(rule =>
                 {
                     if (!bindingLookup.TryGetValue(rule.ModelBindingId, out var bound))
                     {
                         throw new InvalidDataException($"规则引用了不存在的模型绑定：{rule.ModelBindingId}。");
                     }

                     return bound.Binding.ModelId;
                 }))
        {
            var model = project.Models.FirstOrDefault(candidate => candidate.Id == modelGroup.Key)
                ?? throw new InvalidDataException($"模型绑定引用了不存在的模型：{modelGroup.Key}。");
            if (model.Format != ModelFormat.Onnx || model.TaskType != ModelTaskType.Detection)
            {
                throw new NotSupportedException($"模型“{model.Name}”不是当前运行时支持的 ONNX 目标检测模型。");
            }

            var modelPath = ResolveModelPath(model.FilePath, baseDirectory);
            if (!File.Exists(modelPath))
            {
                throw new FileNotFoundException($"模型“{model.Name}”的 ONNX 文件不存在。", modelPath);
            }

            var targets = modelGroup
                .Select(rule =>
                {
                    var bound = bindingLookup[rule.ModelBindingId];
                    return new DetectionBinding(
                        bound.Target.Id,
                        bound.Binding.Id,
                        bound.Binding.OutputLabelId,
                        rule.ConfidenceThreshold);
                })
                .GroupBy(binding => binding.BindingId)
                .Select(group => group.OrderBy(binding => binding.MinimumConfidence).First())
                .ToArray();
            plans.Add(new ModelPlan(model, modelPath, targets));
        }

        return plans;
    }

    private static string ResolveModelPath(string configuredPath, string baseDirectory)
    {
        var expanded = Environment.ExpandEnvironmentVariables(configuredPath);
        return Path.IsPathRooted(expanded)
            ? Path.GetFullPath(expanded)
            : Path.GetFullPath(Path.Combine(baseDirectory, expanded));
    }

    private static void ValidateContract(ModelDefinition model, OnnxModelContract contract)
    {
        if (contract.Inputs.Count != 1 || contract.Outputs.Count != 1)
        {
            throw new NotSupportedException($"模型“{model.Name}”必须恰好包含一个输入和一个输出。");
        }

        var input = contract.Inputs[0];
        if (!input.ElementType.Equals("Float", StringComparison.OrdinalIgnoreCase) ||
            input.Dimensions.Count != 4 || input.Dimensions[0] != 1 || input.Dimensions[1] != 3 ||
            input.Dimensions[2] <= 0 || input.Dimensions[3] <= 0)
        {
            throw new NotSupportedException(
                $"模型“{model.Name}”输入必须是静态 Float[1,3,H,W]，实际为 {input}。");
        }

        var output = contract.Outputs[0];
        if (!output.ElementType.Equals("Float", StringComparison.OrdinalIgnoreCase) ||
            output.Dimensions.Count != 3 || output.Dimensions[0] != 1 || output.Dimensions[1] <= 0 ||
            output.Dimensions[2] != 6)
        {
            throw new NotSupportedException(
                $"模型“{model.Name}”输出必须是端到端 Float[1,N,6]，实际为 {output}。");
        }

        var labelIds = model.Labels.Select(label => label.Id).ToHashSet();
        var invalidBindings = model.Labels.Where(label => label.Id < 0).ToArray();
        if (labelIds.Count == 0 || invalidBindings.Length > 0)
        {
            throw new InvalidDataException($"模型“{model.Name}”缺少有效的非负标签编号。");
        }
    }

    private sealed class ModelRuntime : IDisposable
    {
        private readonly InferenceSession _session;
        private readonly string _inputName;
        private readonly string _outputName;
        private readonly int _inputHeight;
        private readonly int _inputWidth;
        private readonly IReadOnlyDictionary<int, IReadOnlyList<DetectionBinding>> _bindingsByClass;
        private readonly IReadOnlyDictionary<int, double> _minimumConfidenceByClass;
        private readonly IFramePreprocessor _preprocessor;

        public ModelRuntime(ModelPlan plan, IFramePreprocessor preprocessor)
        {
            _preprocessor = preprocessor;
            var options = OnnxModelContractInspector.CreateSessionOptions();
            try
            {
                _session = new InferenceSession(plan.ModelPath, options);
            }
            finally
            {
                options.Dispose();
            }

            var contract = new OnnxModelContract
            {
                Inputs = _session.InputMetadata.Select(pair => new OnnxTensorContract
                {
                    Name = pair.Key,
                    ElementType = pair.Value.ElementDataType.ToString(),
                    Dimensions = pair.Value.Dimensions.ToArray()
                }).ToArray(),
                Outputs = _session.OutputMetadata.Select(pair => new OnnxTensorContract
                {
                    Name = pair.Key,
                    ElementType = pair.Value.ElementDataType.ToString(),
                    Dimensions = pair.Value.Dimensions.ToArray()
                }).ToArray()
            };
            ValidateContract(plan.Model, contract);
            _inputName = contract.Inputs[0].Name;
            _outputName = contract.Outputs[0].Name;
            _inputHeight = contract.Inputs[0].Dimensions[2];
            _inputWidth = contract.Inputs[0].Dimensions[3];
            _bindingsByClass = plan.Bindings
                .GroupBy(binding => binding.LabelId)
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyList<DetectionBinding>)group.ToArray());
            _minimumConfidenceByClass = _bindingsByClass.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.Min(binding => binding.MinimumConfidence));
        }

        public IReadOnlyList<TargetDetection> Infer(ImageFrame frame)
        {
            var preprocessed = _preprocessor.Preprocess(frame, _inputWidth, _inputHeight);
            var tensor = new DenseTensor<float>([1, 3, _inputHeight, _inputWidth]);
            preprocessed.NchwRgb01.AsSpan().CopyTo(tensor.Buffer.Span);
            var input = NamedOnnxValue.CreateFromTensor(_inputName, tensor);
            using var results = _session.Run([input], [_outputName]);
            var output = results.First().AsTensor<float>();
            var parsed = OnnxYoloEndToEndOutputParser.Parse(
                output.ToArray(),
                output.Dimensions.ToArray(),
                preprocessed.Transform,
                _minimumConfidenceByClass);

            return parsed.SelectMany(detection => _bindingsByClass[detection.ClassId].Select(binding =>
                new TargetDetection
                {
                    TargetId = binding.TargetId,
                    ModelBindingId = binding.BindingId,
                    X1 = detection.X1,
                    Y1 = detection.Y1,
                    X2 = detection.X2,
                    Y2 = detection.Y2,
                    Confidence = detection.Confidence
                })).ToArray();
        }

        public void Dispose() => _session.Dispose();

    }

    private sealed record ModelPlan(
        ModelDefinition Model,
        string ModelPath,
        IReadOnlyList<DetectionBinding> Bindings);

    private sealed record BoundTarget(TargetDefinition Target, ModelBindingDefinition Binding);

    private sealed record DetectionBinding(
        Guid TargetId,
        Guid BindingId,
        int LabelId,
        double MinimumConfidence);
}

public sealed record OnnxRuntimeProbe(bool IsReady, string Status);
