using VisualInspection.Core.Configuration;
using VisualInspection.Core.Domain;
using VisualInspection.Core.Rules;

namespace VisualInspection.App.Demo;

public static class SampleProjectFactory
{
    public static readonly Guid SampleProjectId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    public static ProjectConfiguration Create(string? folderPath = null)
    {
        var detectorModelId = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var poseModelId = Guid.Parse("10000000-0000-0000-0000-000000000002");
        var screwTargetId = Guid.Parse("20000000-0000-0000-0000-000000000001");
        var labelTargetId = Guid.Parse("20000000-0000-0000-0000-000000000002");
        var defectTargetId = Guid.Parse("20000000-0000-0000-0000-000000000003");
        var actionTargetId = Guid.Parse("20000000-0000-0000-0000-000000000004");
        var screwBindingId = Guid.Parse("30000000-0000-0000-0000-000000000001");
        var labelBindingId = Guid.Parse("30000000-0000-0000-0000-000000000002");
        var defectBindingId = Guid.Parse("30000000-0000-0000-0000-000000000003");
        var actionBindingId = Guid.Parse("30000000-0000-0000-0000-000000000004");
        var sourceId = Guid.Parse("40000000-0000-0000-0000-000000000001");

        return new ProjectConfiguration
        {
            Id = SampleProjectId,
            Name = "A 线 · 开关装配",
            Workstation = "工位 01",
            Models =
            [
                new ModelDefinition
                {
                    Id = detectorModelId,
                    Name = "检测模型 A",
                    Version = "1.0.0",
                    Format = ModelFormat.Onnx,
                    TaskType = ModelTaskType.Detection,
                    FilePath = "models/detector-a.onnx",
                    LabelSource = LabelSourceMode.ImportedFromModel,
                    Labels =
                    [
                        new ModelLabelDefinition { Id = 0, Name = "螺钉" },
                        new ModelLabelDefinition { Id = 1, Name = "标签" },
                        new ModelLabelDefinition { Id = 2, Name = "瑕疵" }
                    ]
                },
                new ModelDefinition
                {
                    Id = poseModelId,
                    Name = "动作姿态模型",
                    Version = "1.0.0",
                    Format = ModelFormat.Onnx,
                    TaskType = ModelTaskType.Pose,
                    FilePath = "models/action-pose.onnx",
                    LabelSource = LabelSourceMode.Manual,
                    Labels = [new ModelLabelDefinition { Id = 0, Name = "操作员" }]
                }
            ],
            Targets =
            [
                CreateTarget(screwTargetId, "螺钉", screwBindingId, detectorModelId, 0),
                CreateTarget(labelTargetId, "标签", labelBindingId, detectorModelId, 1),
                CreateTarget(defectTargetId, "表面瑕疵", defectBindingId, detectorModelId, 2),
                CreateTarget(actionTargetId, "操作员动作", actionBindingId, poseModelId, 0)
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
                        FolderPath = folderPath ?? "data/sample-set-01",
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
                    Name = "终检测试序列",
                    Version = "V1.4",
                    DefaultDelayMs = 100,
                    InputSourceId = sourceId,
                    SourcePolicy = RuntimeSourcePolicy.Fixed,
                    IsPublished = false,
                    Items =
                    [
                        CreateNormalItem(1, "螺钉在位", new TargetRuleDefinition
                        {
                            TargetId = screwTargetId,
                            ModelBindingId = screwBindingId,
                            Scope = new RegionScopeDefinition
                            {
                                Type = RegionType.Roi,
                                Regions =
                                [
                                    new RegionOfInterestDefinition
                                    {
                                        Name = "区域-A",
                                        X1 = 145,
                                        Y1 = 75,
                                        X2 = 495,
                                        Y2 = 285,
                                        ReferenceWidth = 640,
                                        ReferenceHeight = 360
                                    }
                                ]
                            },
                            Metric = QuantityMetric.PresentCount,
                            Operator = ComparisonOperator.Equal,
                            Threshold = 4,
                            ConfidenceThreshold = 0.6,
                            OutcomeWhenMatched = InspectionVerdict.Pass
                        }),
                        CreateNormalItem(2, "标签对齐", new TargetRuleDefinition
                        {
                            TargetId = labelTargetId,
                            ModelBindingId = labelBindingId,
                            Scope = new RegionScopeDefinition { Type = RegionType.FullImage },
                            Metric = QuantityMetric.PresentCount,
                            Operator = ComparisonOperator.Equal,
                            Threshold = 1,
                            ConfidenceThreshold = 0.7,
                            OutcomeWhenMatched = InspectionVerdict.Pass
                        }),
                        CreateNormalItem(3, "表面瑕疵", new TargetRuleDefinition
                        {
                            TargetId = defectTargetId,
                            ModelBindingId = defectBindingId,
                            Scope = new RegionScopeDefinition { Type = RegionType.FullImage },
                            Metric = QuantityMetric.PresentCount,
                            Operator = ComparisonOperator.GreaterThan,
                            Threshold = 0,
                            ConfidenceThreshold = 0.5,
                            OutcomeWhenMatched = InspectionVerdict.Fail
                        }),
                        new TestItemDefinition
                        {
                            Order = 4,
                            Name = "动作序列",
                            Type = TestItemType.PoseSequence,
                            DelayMs = 150,
                            PoseSteps =
                            [
                                CreatePoseStep(1, "拿取", "hand_near_part", actionBindingId),
                                CreatePoseStep(2, "放置", "hand_near_fixture", actionBindingId),
                                CreatePoseStep(3, "确认", "hands_clear", actionBindingId)
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

    private static TestItemDefinition CreateNormalItem(int order, string name, TargetRuleDefinition rule) =>
        new()
        {
            Order = order,
            Name = name,
            Type = TestItemType.Normal,
            Rules = [rule]
        };

    private static PoseStepDefinition CreatePoseStep(
        int order,
        string name,
        string actionCondition,
        Guid modelBindingId) =>
        new()
        {
            Order = order,
            Name = name,
            ActionCondition = actionCondition,
            ModelBindingId = modelBindingId,
            ConfidenceThreshold = 0.65,
            MinimumHoldMs = 250,
            MaximumWaitMs = 5000
        };
}
