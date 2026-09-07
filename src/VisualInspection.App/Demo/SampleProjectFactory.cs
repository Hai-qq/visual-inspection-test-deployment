using VisualInspection.Core.Configuration;
using VisualInspection.Core.Domain;
using VisualInspection.Core.Rules;

namespace VisualInspection.App.Demo;

public static class SampleProjectFactory
{
    public static readonly Guid SampleProjectId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    public const string SampleProductModel = "FAN-A01";
    public const string SampleProjectName = SampleProductModel + " 视觉检测项目";
    public const string SampleModelName = SampleProductModel + " 线束检测模型";
    public const string BundledFanModelSha256 = "6e30134336323f21a2125bc36b590126b9ac3d2b34ea06ef041f6bdedcb078d7";

    public static ProjectConfiguration Create(string? folderPath = null, string? modelPath = null)
    {
        var fanModelId = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var labelTargetId = Guid.Parse("20000000-0000-0000-0000-000000000001");
        var blackWireTargetId = Guid.Parse("20000000-0000-0000-0000-000000000002");
        var whiteWireTargetId = Guid.Parse("20000000-0000-0000-0000-000000000003");
        var reverseLabelTargetId = Guid.Parse("20000000-0000-0000-0000-000000000004");
        var reverseBlackWireTargetId = Guid.Parse("20000000-0000-0000-0000-000000000005");
        var reverseWhiteWireTargetId = Guid.Parse("20000000-0000-0000-0000-000000000006");
        var labelBindingId = Guid.Parse("30000000-0000-0000-0000-000000000001");
        var blackWireBindingId = Guid.Parse("30000000-0000-0000-0000-000000000002");
        var whiteWireBindingId = Guid.Parse("30000000-0000-0000-0000-000000000003");
        var reverseLabelBindingId = Guid.Parse("30000000-0000-0000-0000-000000000004");
        var reverseBlackWireBindingId = Guid.Parse("30000000-0000-0000-0000-000000000005");
        var reverseWhiteWireBindingId = Guid.Parse("30000000-0000-0000-0000-000000000006");
        var sourceId = Guid.Parse("40000000-0000-0000-0000-000000000001");

        return new ProjectConfiguration
        {
            Id = SampleProjectId,
            Name = SampleProjectName,
            Workstation = "装配线 1 号工位",
            Models =
            [
                new ModelDefinition
                {
                    Id = fanModelId,
                    Name = SampleModelName,
                    Version = "1.0.0",
                    Format = ModelFormat.Onnx,
                    TaskType = ModelTaskType.Detection,
                    FilePath = modelPath ?? "models/fan.onnx",
                    Sha256 = modelPath is null ? null : BundledFanModelSha256,
                    LabelSource = LabelSourceMode.ImportedFromModel,
                    Labels =
                    [
                        new ModelLabelDefinition { Id = 0, Name = "Labell" },
                        new ModelLabelDefinition { Id = 1, Name = "Black_wire" },
                        new ModelLabelDefinition { Id = 2, Name = "white_wire" },
                        new ModelLabelDefinition { Id = 3, Name = "reverse_Labell" },
                        new ModelLabelDefinition { Id = 4, Name = "reverse_Black_wire" },
                        new ModelLabelDefinition { Id = 5, Name = "reverse_white_wire" }
                    ]
                }
            ],
            Targets =
            [
                CreateTarget(labelTargetId, "标签", labelBindingId, fanModelId, 0),
                CreateTarget(blackWireTargetId, "黑线", blackWireBindingId, fanModelId, 1),
                CreateTarget(whiteWireTargetId, "白线", whiteWireBindingId, fanModelId, 2),
                CreateTarget(reverseLabelTargetId, "反向标签", reverseLabelBindingId, fanModelId, 3),
                CreateTarget(reverseBlackWireTargetId, "反向黑线", reverseBlackWireBindingId, fanModelId, 4),
                CreateTarget(reverseWhiteWireTargetId, "反向白线", reverseWhiteWireBindingId, fanModelId, 5)
            ],
            InputSources =
            [
                new InputSourceDefinition
                {
                    Id = sourceId,
                    Name = "内置验收数据",
                    Type = InputSourceType.Folder,
                    Folder = new FolderInputOptions
                    {
                        FolderPath = folderPath ?? "data/fan-pass",
                        IncludeSubfolders = false,
                        SortOrder = FolderSortOrder.NaturalFileName,
                        InvalidFileBehavior = InvalidFileBehavior.Skip,
                        LoopPlayback = false,
                        PoseFrameIntervalMs = 100
                    }
                }
            ],
            TestSequences =
            [
                new TestSequenceDefinition
                {
                    Id = Guid.Parse("50000000-0000-0000-0000-000000000001"),
                    Name = SampleProductModel,
                    Version = "V2.0",
                    DefaultDelayMs = 100,
                    InputSourceId = sourceId,
                    SourcePolicy = RuntimeSourcePolicy.Fixed,
                    IsPublished = modelPath is not null,
                    PublishedAtUtc = modelPath is null
                        ? null
                        : new DateTimeOffset(2026, 8, 14, 0, 0, 0, TimeSpan.Zero),
                    Items =
                    [
                        new TestItemDefinition
                        {
                            Id = Guid.Parse("60000000-0000-0000-0000-000000000001"),
                            Order = 1,
                            Name = "风扇检测",
                            Type = TestItemType.Normal,
                            RuleOperator = RuleLogicalOperator.And,
                            Rules =
                            [
                                CreateCountRule(labelTargetId, labelBindingId, 3),
                                CreateCountRule(blackWireTargetId, blackWireBindingId, 3),
                                CreateCountRule(whiteWireTargetId, whiteWireBindingId, 1),
                                CreateAbsenceRule(reverseLabelTargetId, reverseLabelBindingId),
                                CreateAbsenceRule(reverseBlackWireTargetId, reverseBlackWireBindingId),
                                CreateAbsenceRule(reverseWhiteWireTargetId, reverseWhiteWireBindingId)
                            ]
                        }
                    ]
                }
            ]
        };
    }

    private static TargetDefinition CreateTarget(
        Guid targetId,
        string targetName,
        Guid bindingId,
        Guid modelId,
        int labelId) =>
        new()
        {
            Id = targetId,
            Name = targetName,
            ModelBindings =
            [
                new ModelBindingDefinition
                {
                    Id = bindingId,
                    ModelId = modelId,
                    ModelVersion = "1.0.0",
                    OutputLabelId = labelId
                }
            ]
        };

    private static TargetRuleDefinition CreateCountRule(Guid targetId, Guid bindingId, int expectedCount) =>
        new()
        {
            Id = targetId,
            TargetId = targetId,
            ModelBindingId = bindingId,
            Scope = new RegionScopeDefinition { Type = RegionType.FullImage },
            Metric = QuantityMetric.PresentCount,
            Operator = ComparisonOperator.Equal,
            Threshold = expectedCount,
            ConfidenceThreshold = 0.5,
            OutcomeWhenMatched = InspectionVerdict.Pass
        };

    private static TargetRuleDefinition CreateAbsenceRule(Guid targetId, Guid bindingId) =>
        new()
        {
            Id = targetId,
            TargetId = targetId,
            ModelBindingId = bindingId,
            Scope = new RegionScopeDefinition { Type = RegionType.FullImage },
            Metric = QuantityMetric.PresentCount,
            Operator = ComparisonOperator.GreaterThan,
            Threshold = 0,
            ConfidenceThreshold = 0.5,
            OutcomeWhenMatched = InspectionVerdict.Fail
        };

}
