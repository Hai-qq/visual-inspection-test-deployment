using VisualInspection.Core.Configuration;
using VisualInspection.Core.Domain;
using VisualInspection.Core.Rules;

namespace VisualInspection.Core.Tests;

internal static class ConfigurationTestFactory
{
    public static readonly Guid ModelId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid TargetId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid BindingId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    public static readonly Guid SourceId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    public static ProjectConfiguration Create(ModelTaskType taskType = ModelTaskType.Detection)
    {
        return new ProjectConfiguration
        {
            Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            Name = "Test Project",
            Workstation = "Station A",
            Models =
            [
                new ModelDefinition
                {
                    Id = ModelId,
                    Name = "Model A",
                    Version = "1.0.0",
                    Format = ModelFormat.Onnx,
                    TaskType = taskType,
                    FilePath = "models/model-a.onnx",
                    Sha256 = new string('a', 64),
                    LabelSource = LabelSourceMode.ImportedFromModel,
                    Labels = [new ModelLabelDefinition { Id = 0, Name = "part" }]
                }
            ],
            Targets =
            [
                new TargetDefinition
                {
                    Id = TargetId,
                    Name = "Part",
                    ModelBindings =
                    [
                        new ModelBindingDefinition
                        {
                            Id = BindingId,
                            ModelId = ModelId,
                            ModelVersion = "1.0.0",
                            OutputLabelId = 0
                        }
                    ]
                }
            ],
            InputSources =
            [
                new InputSourceDefinition
                {
                    Id = SourceId,
                    Name = "Input A",
                    Type = InputSourceType.Folder,
                    Folder = new FolderInputOptions { FolderPath = "data/input-a" }
                }
            ],
            TestSequences =
            [
                new TestSequenceDefinition
                {
                    Id = Guid.Parse("55555555-5555-5555-5555-555555555555"),
                    Name = "Sequence A",
                    Version = "V1",
                    DefaultDelayMs = 100,
                    InputSourceId = SourceId,
                    IsPublished = true,
                    PublishedAtUtc = new DateTimeOffset(2026, 8, 7, 0, 0, 0, TimeSpan.Zero),
                    Items =
                    [
                        new TestItemDefinition
                        {
                            Id = Guid.Parse("66666666-6666-6666-6666-666666666666"),
                            Order = 1,
                            Name = "Part Presence",
                            Type = TestItemType.Normal,
                            Rules =
                            [
                                new TargetRuleDefinition
                                {
                                    Id = Guid.Parse("77777777-7777-7777-7777-777777777777"),
                                    TargetId = TargetId,
                                    ModelBindingId = BindingId,
                                    Scope = new RegionScopeDefinition { Type = RegionType.FullImage },
                                    Metric = QuantityMetric.PresentCount,
                                    Operator = ComparisonOperator.Equal,
                                    Threshold = 1,
                                    ConfidenceThreshold = 0.5,
                                    OutcomeWhenMatched = InspectionVerdict.Pass
                                }
                            ]
                        }
                    ]
                }
            ]
        };
    }
}
