using VisualInspection.Core.Configuration;
using VisualInspection.Core.Domain;
using VisualInspection.Core.Execution;
using VisualInspection.Core.Rules;
using VisualInspection.Infrastructure.Analysis;
using VisualInspection.Infrastructure.Imaging;
using Xunit.Abstractions;

namespace VisualInspection.Core.Tests;

public sealed class OnnxFolderBatchExternalProbeTests(ITestOutputHelper output)
{
    [Fact]
    [Trait("Category", "ExternalModel")]
    public async Task Infer_AllImagesWithConfiguredExternalOnnxModel()
    {
        var modelPath = Environment.GetEnvironmentVariable("VISUAL_INSPECTION_ONNX_PROBE");
        var imageFolder = Environment.GetEnvironmentVariable("VISUAL_INSPECTION_IMAGE_PROBE");
        if (string.IsNullOrWhiteSpace(modelPath) || string.IsNullOrWhiteSpace(imageFolder))
        {
            output.WriteLine("外部模型或图像目录未设置，跳过文件夹批量 ONNX 推理检查。");
            return;
        }

        var labels = OnnxModelLabelImporter.Import(modelPath);
        var modelId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var model = new ModelDefinition
        {
            Id = modelId,
            Name = "外部 ONNX 批量探针模型",
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
            ConfidenceThreshold = 0.5,
            OutcomeWhenMatched = target.Name.StartsWith("reverse_", StringComparison.OrdinalIgnoreCase)
                ? InspectionVerdict.Fail
                : InspectionVerdict.Pass
        }).ToList();
        var sourceDefinition = new InputSourceDefinition
        {
            Id = sourceId,
            Name = "外部文件夹批量探针",
            Type = InputSourceType.Folder,
            Folder = new FolderInputOptions { FolderPath = imageFolder }
        };
        var sequence = new TestSequenceDefinition
        {
            Name = "外部 ONNX 文件夹批量探针",
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
            Name = "外部 ONNX 文件夹批量探针",
            Workstation = "test",
            Models = [model],
            Targets = targets,
            InputSources = [sourceDefinition],
            TestSequences = [sequence]
        };

        await using var source = new FolderImageSource(sourceDefinition, AppContext.BaseDirectory);
        using var provider = OnnxYoloInspectionProvider.Create(project, sequence, AppContext.BaseDirectory);
        var result = await new FolderBatchTestSequenceRunner().RunAsync(project, sequence, source, provider);

        foreach (var image in result.Images)
        {
            output.WriteLine(
                $"{image.SourceIndex}/{image.TotalFileCount} {Path.GetFileName(image.FrameOrigin)}: {image.RunResult.Verdict}");
        }

        output.WriteLine(result.Summary);
        Assert.Equal(result.TotalFileCount, result.Images.Count);
        Assert.DoesNotContain(result.Images, image => image.RunResult.Verdict == InspectionVerdict.Error);
        Assert.False(result.WasStopped);
    }
}
