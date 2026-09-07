using System.Globalization;
using System.IO;
using System.Windows;
using VisualInspection.Core.V2.Configuration;

namespace VisualInspection.App.ViewModels.V2;

public static class V2DraftMapper
{
    public static ProjectConfigurationV2 ToProject(TestSequenceWizardV2ViewModel editor)
    {
        ArgumentNullException.ThrowIfNull(editor);
        var profiles = editor.Models.Select(model => new RuntimeProfile
        {
            RuntimeProfileId = model.RuntimeProfileId,
            Name = $"{model.Name} CPU",
            ExecutionProvider = RuntimeExecutionProvider.Cpu,
            IntraOpThreads = 1,
            InterOpThreads = 1,
            WarmupCount = 1,
            MaxConcurrency = 1,
            MemoryBudgetBytes = 512L * 1024 * 1024
        }).ToList();
        var artifacts = editor.Models.Select(ToArtifact).ToList();
        var steps = editor.InspectionItems.Select(ToStep).ToList();
        var sourceKind = editor.SourceKindIndex is 0 or 3 ? InputSourceKind.Folder : InputSourceKind.Camera;
        var source = new InputSourceDefinitionV2
        {
            InputSourceId = editor.InputSourceId,
            SourceBindingId = editor.SourceBindingId,
            Name = editor.SourceKindLabel,
            Kind = sourceKind,
            MaximumFrameAgeMs = 1000
        };
        var sequence = new TestSequenceVersion
        {
            SequenceId = editor.SequenceId,
            Name = editor.SequenceName,
            Version = editor.SequenceVersion,
            DefaultSourceBindingId = editor.SourceBindingId,
            OrderedInvocations = editor.InspectionItems.Select((item, index) => new StepInvocation
            {
                InvocationId = item.InvocationId,
                StepId = item.StepId,
                Order = index + 1,
                IsRequired = item.IsRequired,
                DelayMs = ParseNonNegativeInt(item.CustomFunctionDelayMsText),
                TimeoutOverrideMs = ParsePositiveInt(item.FunctionTimeoutMsText),
                CapturePolicy = ToCapturePolicy(item.FrameInputPolicyIndex),
                FailurePolicy = item.IsRequired
                    ? InvocationFailurePolicy.StopSequence
                    : InvocationFailurePolicy.ContinueSequence
            }).ToList()
        };
        var deployment = ToDeployment(editor, source, steps);
        return new ProjectConfigurationV2
        {
            ProjectId = editor.ProjectId,
            Name = editor.ProjectName,
            Workstation = editor.Workstation,
            RuntimeProfiles = profiles,
            ModelArtifacts = artifacts,
            InputSourceDefinitions = [source],
            TestStepCatalog = steps,
            TestSequenceVersions = [sequence],
            DeploymentBindings = [deployment],
            RetiredFunctionCodes = [.. editor.RetiredFunctionCodes]
        };
    }

    public static void ApplyProject(TestSequenceWizardV2ViewModel editor, ProjectConfigurationV2 project)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(project);
        editor.ProjectId = project.ProjectId;
        editor.ProjectName = project.Name;
        editor.Workstation = project.Workstation;

        editor.Models.Clear();
        foreach (var artifact in project.ModelArtifacts)
        {
            var model = new TestSequenceWizardV2Window.ModelPreview(
                artifact.Name,
                artifact.FilePath ?? artifact.ArtifactUri ?? string.Empty,
                ToModelTypeIndex(artifact.TaskType),
                false,
                artifact.LabelSet.OrderBy(label => label.Id).Select(label => label.Name),
                artifact.ModelArtifactId,
                artifact.RuntimeProfileId,
                artifact.LabelSet.OrderBy(label => label.Id).Select(label => label.Id))
            {
                Version = artifact.Version,
                Sha256 = artifact.Sha256,
                AdapterId = artifact.AdapterId,
                LabelSetVersion = artifact.LabelSetVersion
            };
            editor.Models.Add(model);
        }

        var modelById = editor.Models.ToDictionary(model => model.ModelArtifactId);
        var sequence = project.TestSequenceVersions.OrderByDescending(value => value.PublishedAtUtc).FirstOrDefault();
        var invocationByStepId = sequence?.OrderedInvocations
            .OrderBy(value => value.Order)
            .GroupBy(value => value.StepId)
            .ToDictionary(group => group.Key, group => group.First());
        var orderedSteps = project.TestStepCatalog
            .Select((step, catalogIndex) => new
            {
                Step = step,
                CatalogIndex = catalogIndex,
                ExecutionOrder = invocationByStepId is not null &&
                                 invocationByStepId.TryGetValue(step.StepId, out var invocation)
                    ? invocation.Order
                    : int.MaxValue
            })
            .OrderBy(value => value.ExecutionOrder)
            .ThenBy(value => value.CatalogIndex)
            .Select(value => value.Step);
        editor.InspectionItems.Clear();
        foreach (var step in orderedSteps)
        {
            StepInvocation? invocation = null;
            _ = invocationByStepId?.TryGetValue(step.StepId, out invocation);
            var firstBinding = step.ModelBindings.FirstOrDefault();
            var model = firstBinding is not null && modelById.TryGetValue(firstBinding.ModelArtifactId, out var boundModel)
                ? boundModel
                : editor.Models.FirstOrDefault();
            if (model is null)
            {
                continue;
            }

            var item = new TestSequenceWizardV2Window.InspectionItemPreview(
                step.FunctionCode,
                step.Name,
                step.Kind switch
                {
                    TestStepKind.Pose or TestStepKind.Temporal => 1,
                    TestStepKind.Segmentation => 2,
                    _ => 0
                },
                invocation?.IsRequired ?? true,
                model,
                poseStepNames: [],
                step.StepId,
                firstBinding?.ModelBindingId,
                step.RuleSet?.Rules.FirstOrDefault()?.RuleId,
                step.RuleSet?.Rules.FirstOrDefault()?.Scope.Regions.FirstOrDefault()?.RegionId,
                step.InvocationPolicy.ExternalTriggerBindings.FirstOrDefault()?.BindingId,
                invocation?.InvocationId)
            {
                AllowSequenceInvocation = true,
                AllowManualDebugInvocation = step.InvocationPolicy.AllowManualDebugInvocation,
                ExternalTriggerEnabled = step.InvocationPolicy.ExternalTriggerBindings.Count > 0,
                FunctionTimeoutMsText = (invocation?.TimeoutOverrideMs ?? step.DefaultTimeoutMs).ToString(CultureInfo.InvariantCulture),
                FrameInputPolicyIndex = invocation is null
                    ? ToFramePolicyIndex(step.DefaultFrameInputPolicy)
                    : ToCapturePolicyIndex(invocation.CapturePolicy)
            };
            ApplyTrigger(item, step.InvocationPolicy.ExternalTriggerBindings.FirstOrDefault());
            if (invocation is not null)
            {
                item.CustomFunctionDelayMsText = invocation.DelayMs.ToString(CultureInfo.InvariantCulture);
            }

            if (step.RuleSet is not null)
            {
                ApplyRules(item, step.RuleSet, step.ModelBindings, modelById);
            }

            if (step.PoseProgram is not null)
            {
                ApplyPoseProgram(item, step.PoseProgram, step.ModelBindings, modelById);
            }

            editor.InspectionItems.Add(item);
        }

        if (sequence is not null)
        {
            editor.SequenceId = sequence.SequenceId;
            editor.SequenceName = sequence.Name;
            editor.SequenceVersion = sequence.Version;
        }

        var source = project.InputSourceDefinitions.FirstOrDefault();
        if (source is not null)
        {
            editor.InputSourceId = source.InputSourceId;
            editor.SourceBindingId = source.SourceBindingId;
            editor.SourceKindIndex = source.Kind == InputSourceKind.Folder
                ? string.Equals(source.Name, "视频文件夹", StringComparison.Ordinal) ? 3 : 0
                : source.Name.Contains("USB", StringComparison.OrdinalIgnoreCase) ? 1 : 2;
        }

        var deployment = project.DeploymentBindings.FirstOrDefault();
        if (deployment is not null)
        {
            editor.DeploymentBindingId = deployment.DeploymentBindingId;
            editor.SourceAddress = deployment.InputSourceBindings.FirstOrDefault()?.DeviceAddress ?? string.Empty;
        }

        editor.RetiredFunctionCodes.Clear();
        editor.RetiredFunctionCodes.AddRange(project.RetiredFunctionCodes);
        editor.RefreshSummaries();
    }

    private static ModelArtifact ToArtifact(TestSequenceWizardV2Window.ModelPreview model) => new()
    {
        ModelArtifactId = model.ModelArtifactId,
        Name = model.Name,
        Version = model.Version,
        Format = string.Equals(Path.GetExtension(model.FileName), ".pt", StringComparison.OrdinalIgnoreCase)
            ? ModelArtifactFormat.Pt
            : ModelArtifactFormat.Onnx,
        TaskType = ToTaskType(model.TypeIndex),
        FilePath = model.FileName,
        Sha256 = model.Sha256,
        LabelSet = model.Labels.Select((label, index) => new ModelLabel
        {
            Id = model.LabelIds[index],
            Name = label
        }).ToList(),
        LabelSetVersion = model.LabelSetVersion,
        AdapterId = model.AdapterId,
        RuntimeProfileId = model.RuntimeProfileId
    };

    private static TestStepDefinition ToStep(TestSequenceWizardV2Window.InspectionItemPreview item)
    {
        var isPose = item.TypeIndex == 1;
        var bindings = isPose ? ToPoseBindings(item) : ToRuleBindings(item);
        return new TestStepDefinition
        {
            StepId = item.StepId,
            FunctionCode = item.FunctionCode,
            Name = item.Name,
            Kind = ToTaskType(item.Model.TypeIndex),
            ModelBindings = bindings,
            RuleSet = isPose ? null : ToRuleSet(item, bindings),
            PoseProgram = isPose ? ToPoseProgram(item, bindings) : null,
            InvocationPolicy = new InvocationPolicyDefinition
            {
                AllowSequenceInvocation = true,
                AllowManualDebugInvocation = item.AllowManualDebugInvocation,
                ExternalTriggerBindings = item.ExternalTriggerEnabled
                    ?
                    [
                        new ExternalTriggerBinding
                        {
                            BindingId = item.ExternalTriggerBindingId,
                            LogicalSignalTag = item.TriggerSignal,
                            TriggerCondition = ToTriggerCondition(item.TriggerConditionIndex),
                            DebounceMs = ParseNonNegativeInt(item.TriggerDebounceMsText),
                            PostTriggerDelayMs = ParseNonNegativeInt(item.TriggerDelayMsText),
                            TimeoutMs = ParsePositiveInt(item.FunctionTimeoutMsText),
                            FrameInputPolicy = ToFramePolicy(item.FrameInputPolicyIndex),
                            MaxConcurrency = ParsePositiveInt(item.MaxConcurrencyText),
                            QueueCapacity = ParsePositiveInt(item.QueueCapacityText),
                            OverflowPolicy = item.OverflowPolicyIndex switch
                            {
                                1 => QueueOverflowPolicy.DropOldest,
                                2 => QueueOverflowPolicy.Wait,
                                _ => QueueOverflowPolicy.RejectNewest
                            }
                        }
                    ]
                    : []
            },
            DefaultTimeoutMs = ParsePositiveInt(item.FunctionTimeoutMsText),
            DefaultFrameInputPolicy = ToFramePolicy(item.FrameInputPolicyIndex)
        };
    }

    private static List<ModelBindingV2> ToRuleBindings(TestSequenceWizardV2Window.InspectionItemPreview item)
    {
        var bindings = new List<ModelBindingV2>
        {
            CreateBinding(item.ModelBindingId, item.Model, item.TargetLabel)
        };
        bindings.AddRange(item.AdditionalRules.Select(rule => CreateBinding(rule.ModelBindingId, rule.Model, rule.TargetLabel)));
        return bindings;
    }

    private static List<ModelBindingV2> ToPoseBindings(TestSequenceWizardV2Window.InspectionItemPreview item) =>
        item.PoseSteps.Select(action => CreateBinding(action.ModelBindingId, action.Model, action.ActionCondition)).ToList();

    private static ModelBindingV2 CreateBinding(
        Guid modelBindingId,
        TestSequenceWizardV2Window.ModelPreview model,
        string label) => new()
        {
            ModelBindingId = modelBindingId,
            ModelArtifactId = model.ModelArtifactId,
            ModelVersion = model.Version,
            OutputLabelId = ResolveLabelId(model, label),
            AdapterId = model.AdapterId,
            AdapterProfileId = model.RuntimeProfileId
        };

    private static RuleSetDefinition ToRuleSet(
        TestSequenceWizardV2Window.InspectionItemPreview item,
        IReadOnlyList<ModelBindingV2> bindings)
    {
        var rules = new List<InspectionRuleDefinition>
        {
            CreateRule(
                item.PrimaryRuleId,
                bindings[0],
                item.RuleMetricIndex,
                item.RuleMethodIndex,
                item.ExpectedCountText,
                item.RangeMaximumCountText,
                item.ConfidenceThresholdText,
                item.RuleOutcomeIndex,
                item.UseRoi,
                item.RoiRect,
                item.RoiId,
                item.RoiReferenceWidth,
                item.RoiReferenceHeight)
        };
        rules.AddRange(item.AdditionalRules.Select((rule, index) => CreateRule(
            rule.RuleId,
            bindings[index + 1],
            rule.MetricIndex,
            rule.RuleMethodIndex,
            rule.ThresholdText,
            rule.UpperThresholdText,
            rule.ConfidenceText,
            rule.OutcomeIndex,
            item.UseRoi,
            item.RoiRect,
            item.RoiId,
            item.RoiReferenceWidth,
            item.RoiReferenceHeight)));
        return new RuleSetDefinition
        {
            LogicalOperator = item.RuleLogicalOperatorIndex == 1 ? RuleLogicalOperatorV2.Or : RuleLogicalOperatorV2.And,
            Rules = rules
        };
    }

    private static InspectionRuleDefinition CreateRule(
        Guid ruleId,
        ModelBindingV2 binding,
        int metricIndex,
        int methodIndex,
        string thresholdText,
        string upperText,
        string confidenceText,
        int outcomeIndex,
        bool useRoi,
        Rect roi,
        Guid roiId,
        double roiReferenceWidth,
        double roiReferenceHeight) => new()
        {
            RuleId = ruleId,
            ModelBindingId = binding.ModelBindingId,
            OutputLabelId = binding.OutputLabelId,
            Metric = metricIndex switch
            {
                1 => RuleMetricV2.MissingCount,
                2 => RuleMetricV2.Presence,
                _ => RuleMetricV2.PresentCount
            },
            Operator = methodIndex switch
            {
                1 => RuleComparisonOperatorV2.BetweenInclusive,
                2 => RuleComparisonOperatorV2.GreaterThan,
                3 => RuleComparisonOperatorV2.NotEqual,
                4 => RuleComparisonOperatorV2.GreaterThanOrEqual,
                5 => RuleComparisonOperatorV2.LessThan,
                6 => RuleComparisonOperatorV2.LessThanOrEqual,
                _ => RuleComparisonOperatorV2.Equal
            },
            Threshold = ParseNonNegativeInt(thresholdText),
            UpperThreshold = methodIndex == 1 ? ParseNonNegativeInt(upperText) : null,
            ExpectedCount = metricIndex == 1 ? ParseNonNegativeInt(thresholdText) : null,
            ConfidenceThreshold = ParseConfidence(confidenceText),
            OutcomeWhenMatched = outcomeIndex == 1 ? RuleOutcome.Fail : RuleOutcome.Pass,
            Scope = new RegionScopeDefinitionV2
            {
                Type = useRoi ? RegionScopeTypeV2.Roi : RegionScopeTypeV2.FullImage,
                Regions = useRoi
                ?
                [
                    new RegionOfInterestV2
                    {
                        RegionId = roiId,
                        Name = "主 ROI",
                        X1 = (int)Math.Round(roi.X),
                        Y1 = (int)Math.Round(roi.Y),
                        X2 = (int)Math.Round(roi.Right),
                        Y2 = (int)Math.Round(roi.Bottom),
                        ReferenceWidth = Math.Max(1, (int)Math.Round(roiReferenceWidth)),
                        ReferenceHeight = Math.Max(1, (int)Math.Round(roiReferenceHeight))
                    }
                ]
                : []
            }
        };

    private static PoseProgramDefinition ToPoseProgram(
        TestSequenceWizardV2Window.InspectionItemPreview item,
        IReadOnlyList<ModelBindingV2> bindings) => new()
        {
            ProgramTimeoutMs = Math.Max(ParsePositiveInt(item.FunctionTimeoutMsText),
            item.PoseSteps.Sum(step => ParsePositiveInt(step.MaximumWaitMsText))),
            MaximumFrameGapMs = 500,
            Actions = item.PoseSteps.OrderBy(action => action.Order).Select((action, index) => new PoseActionDefinition
            {
                ActionId = action.ActionId,
                Order = index + 1,
                Name = action.Name,
                ActionCondition = action.ActionCondition,
                ModelBindingId = bindings.Single(binding => binding.ModelBindingId == action.ModelBindingId).ModelBindingId,
                ConfidenceThreshold = ParseConfidence(action.ConfidenceThresholdText),
                MinimumHoldMs = ParseNonNegativeInt(action.MinimumHoldMsText),
                MaximumWaitMs = ParsePositiveInt(action.MaximumWaitMsText),
                IsRequired = action.IsRequired
            }).ToList()
        };

    private static DeploymentBinding ToDeployment(
        TestSequenceWizardV2ViewModel editor,
        InputSourceDefinitionV2 source,
        IEnumerable<TestStepDefinition> steps)
    {
        var triggerBindings = steps.SelectMany(step => step.InvocationPolicy.ExternalTriggerBindings)
            .GroupBy(binding => binding.LogicalSignalTag, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Where(binding => !string.IsNullOrWhiteSpace(binding.LogicalSignalTag))
            .Select(binding => new TriggerSignalDeploymentBinding
            {
                BindingId = binding.BindingId,
                LogicalSignalTag = binding.LogicalSignalTag,
                TriggerAdapterId = "trigger-simulator",
                Protocol = "Simulator",
                ConnectionProfileId = "acceptance-trigger",
                DeviceAddress = binding.LogicalSignalTag
            }).ToList();
        return new DeploymentBinding
        {
            DeploymentBindingId = editor.DeploymentBindingId,
            Name = $"{editor.Workstation} Deployment",
            StationId = editor.Workstation,
            TriggerSignalBindings = triggerBindings,
            InputSourceBindings =
            [
                new InputSourceDeploymentBinding
                {
                    SourceBindingId = source.SourceBindingId,
                    InputSourceId = source.InputSourceId,
                    CameraAdapterId = KnownAdapterIds.SimulatedCamera,
                    ConnectionProfileId = "acceptance-source",
                    DeviceAddress = editor.SourceAddress
                }
            ],
            LineResultBinding = new LineResultDeploymentBinding
            {
                AdapterId = KnownAdapterIds.SimulatedLineResult,
                ConnectionProfileId = "acceptance-line",
                LogicalPointMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Ready"] = "SIM.Ready",
                    ["Busy"] = "SIM.Busy",
                    ["Pass"] = "SIM.Pass",
                    ["Fail"] = "SIM.Fail",
                    ["Error"] = "SIM.Error",
                    ["Ack"] = "SIM.Ack"
                }
            },
            AdapterAllowlist = editor.Models.Select(model => model.AdapterId)
                .Append(KnownAdapterIds.SimulatedCamera)
                .Append("trigger-simulator")
                .Append(KnownAdapterIds.SimulatedLineResult)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
    }

    private static void ApplyTrigger(
        TestSequenceWizardV2Window.InspectionItemPreview item,
        ExternalTriggerBinding? binding)
    {
        if (binding is null)
        {
            return;
        }

        item.TriggerSignal = binding.LogicalSignalTag;
        item.TriggerConditionIndex = binding.TriggerCondition switch
        {
            TriggerCondition.FallingEdge => 1,
            TriggerCondition.HighLevel => 2,
            TriggerCondition.LowLevel => 3,
            _ => 0
        };
        item.TriggerDebounceMsText = binding.DebounceMs.ToString(CultureInfo.InvariantCulture);
        item.TriggerDelayMsText = binding.PostTriggerDelayMs.ToString(CultureInfo.InvariantCulture);
        item.MaxConcurrencyText = binding.MaxConcurrency.ToString(CultureInfo.InvariantCulture);
        item.QueueCapacityText = binding.QueueCapacity.ToString(CultureInfo.InvariantCulture);
        item.OverflowPolicyIndex = binding.OverflowPolicy switch
        {
            QueueOverflowPolicy.DropOldest => 1,
            QueueOverflowPolicy.Wait => 2,
            _ => 0
        };
    }

    private static void ApplyRules(
        TestSequenceWizardV2Window.InspectionItemPreview item,
        RuleSetDefinition ruleSet,
        IReadOnlyList<ModelBindingV2> bindings,
        IReadOnlyDictionary<Guid, TestSequenceWizardV2Window.ModelPreview> modelById)
    {
        item.RuleLogicalOperatorIndex = ruleSet.LogicalOperator == RuleLogicalOperatorV2.Or ? 1 : 0;
        for (var index = 0; index < ruleSet.Rules.Count; index++)
        {
            var rule = ruleSet.Rules[index];
            var binding = bindings.FirstOrDefault(value => value.ModelBindingId == rule.ModelBindingId);
            var model = binding is not null && modelById.TryGetValue(binding.ModelArtifactId, out var selectedModel)
                ? selectedModel
                : item.Model;
            var target = model.ResolveLabelName(rule.OutputLabelId) ?? string.Empty;
            if (index == 0)
            {
                item.Model = model;
                item.TargetLabel = target;
                item.RuleMetricIndex = ToRuleMetricIndex(rule.Metric);
                item.RuleMethodIndex = ToRuleMethodIndex(rule.Operator);
                item.ExpectedCountText = rule.Threshold.ToString(CultureInfo.InvariantCulture);
                item.RangeMaximumCountText = (rule.UpperThreshold ?? rule.Threshold).ToString(CultureInfo.InvariantCulture);
                item.ConfidenceThresholdText = rule.ConfidenceThreshold.ToString("0.##", CultureInfo.InvariantCulture);
                item.RuleOutcomeIndex = rule.OutcomeWhenMatched == RuleOutcome.Fail ? 1 : 0;
                ApplyScope(item, rule.Scope);
                continue;
            }

            item.AdditionalRules.Add(new RulePreviewViewModel(target, model, rule.RuleId, rule.ModelBindingId)
            {
                MetricIndex = ToRuleMetricIndex(rule.Metric),
                RuleMethodIndex = ToRuleMethodIndex(rule.Operator),
                ThresholdText = rule.Threshold.ToString(CultureInfo.InvariantCulture),
                UpperThresholdText = (rule.UpperThreshold ?? rule.Threshold).ToString(CultureInfo.InvariantCulture),
                ConfidenceText = rule.ConfidenceThreshold.ToString("0.##", CultureInfo.InvariantCulture),
                OutcomeIndex = rule.OutcomeWhenMatched == RuleOutcome.Fail ? 1 : 0
            });
        }
    }

    private static void ApplyPoseProgram(
        TestSequenceWizardV2Window.InspectionItemPreview item,
        PoseProgramDefinition poseProgram,
        IReadOnlyList<ModelBindingV2> bindings,
        IReadOnlyDictionary<Guid, TestSequenceWizardV2Window.ModelPreview> modelById)
    {
        item.PoseSteps.Clear();
        foreach (var action in poseProgram.Actions.OrderBy(value => value.Order))
        {
            var binding = bindings.FirstOrDefault(value => value.ModelBindingId == action.ModelBindingId);
            var model = binding is not null && modelById.TryGetValue(binding.ModelArtifactId, out var actionModel)
                ? actionModel
                : item.Model;
            item.PoseSteps.Add(new TestSequenceWizardV2Window.PoseStepPreview(
                action.Order,
                action.Name,
                action.IsRequired,
                model,
                action.ActionId,
                action.ModelBindingId)
            {
                ActionCondition = action.ActionCondition,
                ConfidenceThresholdText = action.ConfidenceThreshold.ToString("0.##", CultureInfo.InvariantCulture),
                MinimumHoldMsText = action.MinimumHoldMs.ToString(CultureInfo.InvariantCulture),
                MaximumWaitMsText = action.MaximumWaitMs.ToString(CultureInfo.InvariantCulture)
            });
        }

        item.PoseActionIndex = item.PoseSteps.Count > 0 ? 0 : -1;
    }

    private static void ApplyScope(
        TestSequenceWizardV2Window.InspectionItemPreview item,
        RegionScopeDefinitionV2 scope)
    {
        item.UseRoi = scope.Type == RegionScopeTypeV2.Roi;
        var roi = scope.Regions.FirstOrDefault();
        if (roi is not null)
        {
            item.RoiReferenceWidth = roi.ReferenceWidth;
            item.RoiReferenceHeight = roi.ReferenceHeight;
            item.RoiRect = new Rect(roi.X1, roi.Y1, roi.X2 - roi.X1, roi.Y2 - roi.Y1);
            if (item.NamedRois.Count > 0)
            {
                item.NamedRois[0].Rect = item.RoiRect;
            }
        }
    }

    private static int ResolveLabelId(TestSequenceWizardV2Window.ModelPreview model, string label) =>
        model.ResolveLabelId(label);

    private static TestStepKind ToTaskType(int typeIndex) => typeIndex switch
    {
        1 => TestStepKind.Temporal,
        2 => TestStepKind.Classification,
        3 => TestStepKind.Segmentation,
        _ => TestStepKind.Detection
    };

    private static int ToModelTypeIndex(TestStepKind kind) => kind switch
    {
        TestStepKind.Pose or TestStepKind.Temporal => 1,
        TestStepKind.Classification => 2,
        TestStepKind.Segmentation => 3,
        _ => 0
    };

    private static TriggerCondition ToTriggerCondition(int index) => index switch
    {
        1 => TriggerCondition.FallingEdge,
        2 => TriggerCondition.HighLevel,
        3 => TriggerCondition.LowLevel,
        _ => TriggerCondition.RisingEdge
    };

    private static FrameInputPolicy ToFramePolicy(int index) => index switch
    {
        1 => FrameInputPolicy.CapturePerInvocation,
        2 => FrameInputPolicy.ContinuousStream,
        3 => FrameInputPolicy.ExternalContextFrame,
        4 => FrameInputPolicy.OperatorDebugSelection,
        _ => FrameInputPolicy.CaptureOncePerProduct
    };

    private static int ToFramePolicyIndex(FrameInputPolicy policy) => policy switch
    {
        FrameInputPolicy.CapturePerInvocation => 1,
        FrameInputPolicy.ContinuousStream => 2,
        FrameInputPolicy.ExternalContextFrame => 3,
        FrameInputPolicy.OperatorDebugSelection => 4,
        _ => 0
    };

    private static CapturePolicy ToCapturePolicy(int index) => index switch
    {
        1 => CapturePolicy.CapturePerInvocation,
        2 => CapturePolicy.ContinuousStream,
        3 => CapturePolicy.ExternalContextFrame,
        _ => CapturePolicy.CaptureOncePerProduct
    };

    private static int ToCapturePolicyIndex(CapturePolicy policy) => policy switch
    {
        CapturePolicy.CapturePerInvocation => 1,
        CapturePolicy.ContinuousStream => 2,
        CapturePolicy.ExternalContextFrame => 3,
        _ => 0
    };

    private static int ToRuleMethodIndex(RuleComparisonOperatorV2 value) => value switch
    {
        RuleComparisonOperatorV2.BetweenInclusive => 1,
        RuleComparisonOperatorV2.GreaterThan => 2,
        RuleComparisonOperatorV2.NotEqual => 3,
        RuleComparisonOperatorV2.GreaterThanOrEqual => 4,
        RuleComparisonOperatorV2.LessThan => 5,
        RuleComparisonOperatorV2.LessThanOrEqual => 6,
        _ => 0
    };

    private static int ToRuleMetricIndex(RuleMetricV2 value) => value switch
    {
        RuleMetricV2.MissingCount => 1,
        RuleMetricV2.Presence => 2,
        _ => 0
    };

    private static int ParsePositiveInt(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0 ? parsed : 0;

    private static int ParseNonNegativeInt(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed >= 0 ? parsed : -1;

    private static double ParseConfidence(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? 0.5
            : double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : -1;

}
