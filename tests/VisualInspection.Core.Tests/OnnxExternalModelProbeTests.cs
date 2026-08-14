using VisualInspection.Infrastructure.Analysis;
using VisualInspection.Core.Configuration;
using VisualInspection.Core.Domain;
using VisualInspection.Core.Execution;
using VisualInspection.Core.Imaging;
using VisualInspection.Core.Rules;
using VisualInspection.Infrastructure.Imaging;
using Xunit.Abstractions;

namespace VisualInspection.Core.Tests;

public sealed class OnnxExternalModelProbeTests(ITestOutputHelper output)
{
    [Fact]
    [Trait("Category", "ExternalModel")]
    public void Inspect_ConfiguredExternalOnnxModel()
    {
        var modelPath = Environment.GetEnvironmentVariable("VISUAL_INSPECTION_ONNX_PROBE");
        if (string.IsNullOrWhiteSpace(modelPath))
        {
            output.WriteLine("VISUAL_INSPECTION_ONNX_PROBE 未设置，跳过外部模型契约检查。");
            return;
        }

        var contract = OnnxModelContractInspector.Inspect(modelPath);
        output.WriteLine($"Producer: {contract.ProducerName}");
        output.WriteLine($"Graph: {contract.GraphName}");
        output.WriteLine($"Inputs: {string.Join("; ", contract.Inputs)}");
        output.WriteLine($"Outputs: {string.Join("; ", contract.Outputs)}");
        foreach (var pair in contract.CustomMetadata.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            output.WriteLine($"Metadata[{pair.Key}]={pair.Value}");
        }

        Assert.Single(contract.Inputs);
        Assert.NotEmpty(contract.Outputs);
    }

    [Fact]
    [Trait("Category", "ExternalModel")]
    public async Task Infer_FirstImageWithConfiguredExternalOnnxModel()
    {
        var modelPath = Environment.GetEnvironmentVariable("VISUAL_INSPECTION_ONNX_PROBE");
        var imageFolder = Environment.GetEnvironmentVariable("VISUAL_INSPECTION_IMAGE_PROBE");
        if (string.IsNullOrWhiteSpace(modelPath) || string.IsNullOrWhiteSpace(imageFolder))
        {
            output.WriteLine("外部模型或图像目录未设置，跳过真实 ONNX 推理检查。");
            return;
        }

        var labels = OnnxModelLabelImporter.Import(modelPath);
        var modelId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var model = new ModelDefinition
        {
            Id = modelId,
            Name = "外部 ONNX 探针模型",
            Version = "probe",
            Format = ModelFormat.Onnx,
            TaskType = ModelTaskType.Detection,
            FilePath = modelPath,
            LabelSource = LabelSourceMode.ImportedFromModel,
            Labels = labels.ToList()
        };
        var targets = labels.Select(label =>
        {
            var binding = new ModelBindingDefinition
            {
                ModelId = modelId,
                ModelVersion = model.Version,
                OutputLabelId = label.Id
            };
            return new TargetDefinition
            {
                Name = label.Name,
                ModelBindings = [binding]
            };
        }).ToList();
        var rules = targets.Select(target => new TargetRuleDefinition
        {
            TargetId = target.Id,
            ModelBindingId = target.ModelBindings[0].Id,
            Scope = new RegionScopeDefinition { Type = RegionType.FullImage },
            Metric = QuantityMetric.PresentCount,
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 0,
            ConfidenceThreshold = 0.25,
            OutcomeWhenMatched = target.Name.StartsWith("reverse_", StringComparison.OrdinalIgnoreCase)
                ? InspectionVerdict.Fail
                : InspectionVerdict.Pass
        }).ToList();
        var sourceDefinition = new InputSourceDefinition
        {
            Id = sourceId,
            Name = "外部图像探针",
            Type = InputSourceType.Folder,
            Folder = new FolderInputOptions { FolderPath = imageFolder }
        };
        var sequence = new TestSequenceDefinition
        {
            Name = "外部 ONNX 探针",
            Version = "probe",
            InputSourceId = sourceId,
            Items =
            [
                new TestItemDefinition
                {
                    Order = 1,
                    Name = "全标签探针",
                    Type = TestItemType.Normal,
                    RuleOperator = RuleLogicalOperator.And,
                    Rules = rules
                }
            ]
        };
        var project = new ProjectConfiguration
        {
            Name = "外部 ONNX 探针",
            Workstation = "test",
            Models = [model],
            Targets = targets,
            InputSources = [sourceDefinition],
            TestSequences = [sequence]
        };

        await using var source = new FolderImageSource(sourceDefinition, AppContext.BaseDirectory);
        await source.OpenAsync();
        var frame = await source.ReadAsync() ?? throw new InvalidDataException("测试目录没有可读取的图像。");
        using var provider = OnnxYoloInspectionProvider.Create(project, sequence, AppContext.BaseDirectory);
        var observation = await provider.AnalyzeAsync(frame);
        output.WriteLine($"Frame: {Path.GetFileName(frame.Origin)} {frame.Width}x{frame.Height}");
        output.WriteLine($"Provider: {observation.ProviderDetails}");
        foreach (var detection in observation.Detections ?? [])
        {
            var targetName = targets.First(target => target.Id == detection.TargetId).Name;
            output.WriteLine(
                $"Detection: {targetName} confidence={detection.Confidence:F4} box=({detection.X1:F1},{detection.Y1:F1})-({detection.X2:F1},{detection.Y2:F1})");
        }

        Assert.NotNull(observation.Detections);
        Assert.NotEmpty(observation.Detections);

        var runResult = await new TestSequenceRunner().RunAsync(project, sequence, source, provider);
        output.WriteLine($"Run: {runResult.Verdict} · {runResult.Summary}");
        Assert.Equal(InspectionVerdict.Pass, runResult.Verdict);
    }
}
