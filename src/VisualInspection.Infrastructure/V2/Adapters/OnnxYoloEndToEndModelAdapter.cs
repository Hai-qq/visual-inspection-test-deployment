using System.Collections.Concurrent;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using VisualInspection.Core.V2.Configuration;
using VisualInspection.Core.V2.Execution;
using VisualInspection.Infrastructure.Analysis;

namespace VisualInspection.Infrastructure.V2.Adapters;

public sealed class OnnxYoloEndToEndModelAdapter : IModelRuntimeAdapter, IDisposable
{
    private readonly ConcurrentDictionary<RuntimeKey, Lazy<ModelSession>> _sessions = new();
    private readonly IFramePreprocessor _preprocessor;
    private bool _disposed;

    public OnnxYoloEndToEndModelAdapter(IFramePreprocessor? preprocessor = null)
    {
        _preprocessor = preprocessor ?? new SkiaFramePreprocessor();
    }

    public string AdapterId => KnownAdapterIds.YoloEndToEndDetection;
    public RuntimeProviderKind ProviderKind => RuntimeProviderKind.OnnxYoloEndToEnd;
    public TestStepKind TaskType => TestStepKind.Detection;
    public bool SupportsHardCancellation => false;

    public Task<AdapterProbeResult> ProbeAsync(
        ModelArtifact artifact,
        RuntimeProfile runtimeProfile,
        string baseDirectory,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            ValidateArtifact(artifact, runtimeProfile);
            var path = ResolvePath(artifact, baseDirectory);
            var contract = OnnxModelContractInspector.Inspect(path);
            ValidateContract(artifact.Name, contract);
            return Task.FromResult(AdapterProbeResult.Ready(
                "ONNX Runtime CPU · YOLO End-to-End Float[1,3,H,W] → Float[1,N,6] 已就绪。"));
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or InvalidDataException or
                                           InvalidOperationException or NotSupportedException or OnnxRuntimeException)
        {
            return Task.FromResult(AdapterProbeResult.NotReady(new StructuredError
            {
                Category = ErrorCategory.ModelLoad,
                Code = "ONNX_E2E_PROBE_FAILED",
                Message = exception.Message
            }));
        }
    }

    public async Task<ModelObservation> ExecuteAsync(
        ModelExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(request);
        ValidateArtifact(request.Artifact, request.RuntimeProfile);
        var path = ResolvePath(
            request.Artifact,
            string.IsNullOrWhiteSpace(request.BaseDirectory) ? Directory.GetCurrentDirectory() : request.BaseDirectory);
        var key = new RuntimeKey(
            request.Artifact.ModelArtifactId,
            request.Artifact.Sha256,
            request.RuntimeProfile.RuntimeProfileId,
            path);
        var session = _sessions.GetOrAdd(
            key,
            _ => new Lazy<ModelSession>(
                () => new ModelSession(path, request.Artifact.Name, request.RuntimeProfile, _preprocessor),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;
        return await session.ExecuteAsync(request, cancellationToken);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (var session in _sessions.Values.Where(value => value.IsValueCreated))
        {
            session.Value.Dispose();
        }

        _sessions.Clear();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private static void ValidateArtifact(ModelArtifact artifact, RuntimeProfile runtimeProfile)
    {
        if (artifact.Format != ModelArtifactFormat.Onnx || artifact.TaskType != TestStepKind.Detection ||
            !string.Equals(artifact.AdapterId, KnownAdapterIds.YoloEndToEndDetection, StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException("该 Adapter 只支持显式标记的 ONNX YOLO End-to-End Detection Artifact。");
        }

        if (runtimeProfile.ExecutionProvider != RuntimeExecutionProvider.Cpu)
        {
            throw new NotSupportedException($"当前 Adapter 只实现 CPU Execution Provider；{runtimeProfile.ExecutionProvider} Profile 保持 fail-closed。");
        }
    }

    private static string ResolvePath(ModelArtifact artifact, string baseDirectory)
    {
        if (!string.IsNullOrWhiteSpace(artifact.FilePath))
        {
            var expanded = Environment.ExpandEnvironmentVariables(artifact.FilePath);
            return Path.IsPathRooted(expanded)
                ? Path.GetFullPath(expanded)
                : Path.GetFullPath(Path.Combine(baseDirectory, expanded));
        }

        if (Uri.TryCreate(artifact.ArtifactUri, UriKind.Absolute, out var uri) && uri.IsFile)
        {
            return uri.LocalPath;
        }

        throw new NotSupportedException("当前 Adapter 只支持本地 FilePath 或 file:// Artifact URI。");
    }

    private static void ValidateContract(string modelName, OnnxModelContract contract)
    {
        if (contract.Inputs.Count != 1 || contract.Outputs.Count != 1)
        {
            throw new NotSupportedException($"模型“{modelName}”必须恰好有一个输入和一个输出。");
        }

        var input = contract.Inputs[0];
        if (!input.ElementType.Equals("Float", StringComparison.OrdinalIgnoreCase) ||
            input.Dimensions.Count != 4 || input.Dimensions[0] != 1 || input.Dimensions[1] != 3 ||
            input.Dimensions[2] <= 0 || input.Dimensions[3] <= 0)
        {
            throw new NotSupportedException($"输入必须是静态 Float[1,3,H,W]，实际为 {input}。");
        }

        var output = contract.Outputs[0];
        if (!output.ElementType.Equals("Float", StringComparison.OrdinalIgnoreCase) ||
            output.Dimensions.Count != 3 || output.Dimensions[0] != 1 || output.Dimensions[1] <= 0 ||
            output.Dimensions[2] != 6)
        {
            throw new NotSupportedException($"输出必须是 Float[1,N,6]，实际为 {output}。");
        }
    }

    private sealed class ModelSession : IDisposable
    {
        private readonly InferenceSession _session;
        private readonly SemaphoreSlim _concurrency;
        private readonly IFramePreprocessor _preprocessor;
        private readonly string _inputName;
        private readonly string _outputName;
        private readonly int _inputHeight;
        private readonly int _inputWidth;

        public ModelSession(
            string path,
            string modelName,
            RuntimeProfile profile,
            IFramePreprocessor preprocessor)
        {
            _preprocessor = preprocessor;
            _concurrency = new SemaphoreSlim(profile.MaxConcurrency, profile.MaxConcurrency);
            using var options = OnnxModelContractInspector.CreateSessionOptions(
                profile.IntraOpThreads,
                profile.InterOpThreads);
            _session = new InferenceSession(path, options);
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
            ValidateContract(modelName, contract);
            _inputName = contract.Inputs[0].Name;
            _outputName = contract.Outputs[0].Name;
            _inputHeight = contract.Inputs[0].Dimensions[2];
            _inputWidth = contract.Inputs[0].Dimensions[3];
            WarmUp(profile.WarmupCount);
        }

        public async Task<ModelObservation> ExecuteAsync(
            ModelExecutionRequest request,
            CancellationToken cancellationToken)
        {
            await _concurrency.WaitAsync(cancellationToken);
            try
            {
                return await Task.Run(() => Infer(request), CancellationToken.None);
            }
            finally
            {
                _concurrency.Release();
            }
        }

        public void Dispose()
        {
            _session.Dispose();
            _concurrency.Dispose();
        }

        private ModelObservation Infer(ModelExecutionRequest request)
        {
            var preprocessed = _preprocessor.Preprocess(request.Frame.ImageFrame, _inputWidth, _inputHeight);
            var tensor = new DenseTensor<float>([1, 3, _inputHeight, _inputWidth]);
            preprocessed.NchwRgb01.AsSpan().CopyTo(tensor.Buffer.Span);
            var input = NamedOnnxValue.CreateFromTensor(_inputName, tensor);
            using var results = _session.Run([input], [_outputName]);
            var output = results.First().AsTensor<float>();
            var parsed = OnnxYoloEndToEndOutputParser.Parse(
                output.ToArray(),
                output.Dimensions.ToArray(),
                preprocessed.Transform,
                request.Artifact.LabelSet.ToDictionary(label => label.Id, _ => 0d));
            var detections = parsed.Select(detection => new ModelDetectionV2
            {
                ModelBindingId = request.Binding.ModelBindingId,
                OutputLabelId = detection.ClassId,
                X1 = detection.X1,
                Y1 = detection.Y1,
                X2 = detection.X2,
                Y2 = detection.Y2,
                Confidence = detection.Confidence
            }).ToArray();
            return new ModelObservation
            {
                CaptureId = request.Frame.CaptureId,
                ModelBindingId = request.Binding.ModelBindingId,
                ObservedAtUtc = DateTimeOffset.UtcNow,
                Counts = request.Artifact.LabelSet.ToDictionary(
                    label => new ModelOutputKey(request.Binding.ModelBindingId, label.Id),
                    label => detections.Count(detection => detection.OutputLabelId == label.Id)),
                Detections = detections,
                FrameWidth = request.Frame.ImageFrame.Width,
                FrameHeight = request.Frame.ImageFrame.Height,
                ProviderDetails = "ONNX Runtime CPU · YOLO End-to-End Detection"
            };
        }

        private void WarmUp(int count)
        {
            if (count <= 0)
            {
                return;
            }

            var tensor = new DenseTensor<float>([1, 3, _inputHeight, _inputWidth]);
            for (var index = 0; index < count; index++)
            {
                var input = NamedOnnxValue.CreateFromTensor(_inputName, tensor);
                using var results = _session.Run([input], [_outputName]);
            }
        }
    }

    private readonly record struct RuntimeKey(
        Guid ModelArtifactId,
        string Sha256,
        Guid RuntimeProfileId,
        string Path);
}
