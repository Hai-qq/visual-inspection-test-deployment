using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Legacy = VisualInspection.Core.Configuration;
using LegacyDomain = VisualInspection.Core.Domain;
using LegacyRules = VisualInspection.Core.Rules;

namespace VisualInspection.Core.V2.Configuration;

public static class ProjectConfigurationV1Migrator
{
    public static ProjectConfigurationV2 Migrate(Legacy.ProjectConfiguration source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var profiles = source.Models.Select(model => new RuntimeProfile
        {
            RuntimeProfileId = DeriveGuid(model.Id, "runtime-profile"),
            Name = $"{model.Name} CPU",
            ExecutionProvider = RuntimeExecutionProvider.Cpu,
            IntraOpThreads = 1,
            InterOpThreads = 1,
            WarmupCount = 1,
            MaxConcurrency = 1,
            MemoryBudgetBytes = 512L * 1024 * 1024
        }).ToList();
        var profileByModel = source.Models.Zip(profiles).ToDictionary(pair => pair.First.Id, pair => pair.Second);

        var artifacts = source.Models.Select(model => new ModelArtifact
        {
            ModelArtifactId = model.Id,
            Name = model.Name,
            Version = model.Version,
            Format = model.Format == Legacy.ModelFormat.Onnx ? ModelArtifactFormat.Onnx : ModelArtifactFormat.Pt,
            TaskType = MapTaskType(model.TaskType),
            FilePath = model.FilePath,
            Sha256 = model.Sha256 ?? string.Empty,
            LabelSet = model.Labels.Select(label => new ModelLabel { Id = label.Id, Name = label.Name }).ToList(),
            LabelSetVersion = string.IsNullOrWhiteSpace(model.Version) ? "legacy-v1" : model.Version,
            AdapterId = GetAdapterId(model),
            RuntimeProfileId = profileByModel[model.Id].RuntimeProfileId
        }).ToList();
        var artifactById = artifacts.ToDictionary(artifact => artifact.ModelArtifactId);

        var inputSources = source.InputSources.Select(input => new InputSourceDefinitionV2
        {
            InputSourceId = input.Id,
            Name = input.Name,
            Kind = input.Type == Legacy.InputSourceType.Folder ? InputSourceKind.Folder : InputSourceKind.Camera,
            SourceBindingId = input.Id,
            MaximumFrameAgeMs = input.Camera?.GrabTimeoutMs ?? 1000
        }).ToList();

        var legacyBindingLookup = source.Targets
            .SelectMany(target => target.ModelBindings)
            .GroupBy(binding => binding.Id)
            .ToDictionary(group => group.Key, group => group.First());

        var steps = new Dictionary<Guid, TestStepDefinition>();
        foreach (var item in source.TestSequences.SelectMany(sequence => sequence.Items).OrderBy(item => item.Id))
        {
            if (steps.ContainsKey(item.Id))
            {
                continue;
            }

            var usedBindingIds = item.Rules.Select(rule => rule.ModelBindingId)
                .Concat(item.PoseSteps.Select(action => action.ModelBindingId))
                .Distinct()
                .ToArray();
            var bindings = usedBindingIds
                .Where(legacyBindingLookup.ContainsKey)
                .Select(bindingId =>
                {
                    var binding = legacyBindingLookup[bindingId];
                    var artifact = artifactById[binding.ModelId];
                    return new ModelBindingV2
                    {
                        ModelBindingId = binding.Id,
                        ModelArtifactId = binding.ModelId,
                        ModelVersion = binding.ModelVersion,
                        OutputLabelId = binding.OutputLabelId,
                        AdapterId = artifact.AdapterId,
                        AdapterProfileId = artifact.RuntimeProfileId
                    };
                })
                .ToList();

            var stepKind = item.Type == Legacy.TestItemType.PoseSequence
                ? TestStepKind.Pose
                : bindings.Select(binding => artifactById[binding.ModelArtifactId].TaskType).FirstOrDefault();
            steps[item.Id] = new TestStepDefinition
            {
                StepId = item.Id,
                FunctionCode = FunctionCodeCatalog.CreateStableCode(item.Name, item.Id),
                Name = item.Name,
                Kind = stepKind,
                ModelBindings = bindings,
                RuleSet = item.Type == Legacy.TestItemType.Normal ? MapRuleSet(item, legacyBindingLookup) : null,
                PoseProgram = item.Type == Legacy.TestItemType.PoseSequence ? MapPoseProgram(item) : null,
                InvocationPolicy = new InvocationPolicyDefinition
                {
                    AllowSequenceInvocation = true,
                    AllowManualDebugInvocation = true
                },
                DefaultTimeoutMs = item.Type == Legacy.TestItemType.PoseSequence
                    ? Math.Max(5000, item.PoseSteps.Sum(action => action.MaximumWaitMs))
                    : 5000,
                DefaultFrameInputPolicy = FrameInputPolicy.CaptureOncePerProduct
            };
        }

        var sequences = source.TestSequences.Select(sequence =>
        {
            var migrated = new TestSequenceVersion
            {
                SequenceId = sequence.Id,
                Version = sequence.Version,
                Name = sequence.Name,
                IsPublished = sequence.IsPublished,
                PublishedAtUtc = sequence.PublishedAtUtc,
                DefaultSourceBindingId = sequence.InputSourceId,
                OrderedInvocations = sequence.Items
                    .Where(item => item.Enabled)
                    .OrderBy(item => item.Order)
                    .Select((item, index) => new StepInvocation
                    {
                        InvocationId = DeriveGuid(sequence.Id, $"invocation-{item.Id:N}-{index}"),
                        StepId = item.Id,
                        Order = index + 1,
                        IsRequired = item.IsRequired,
                        DelayMs = item.DelayMs ?? sequence.DefaultDelayMs,
                        CapturePolicy = CapturePolicy.CaptureOncePerProduct,
                        FailurePolicy = item.IsRequired
                            ? InvocationFailurePolicy.StopSequence
                            : InvocationFailurePolicy.ContinueSequence
                    })
                    .ToList()
            };

            return migrated.IsPublished
                ? migrated with { ContentHash = ComputeSequenceHash(migrated) }
                : migrated;
        }).ToList();

        return new ProjectConfigurationV2
        {
            ProjectId = source.Id,
            Name = source.Name,
            Workstation = source.Workstation,
            ModelArtifacts = artifacts,
            RuntimeProfiles = profiles,
            InputSourceDefinitions = inputSources,
            TestStepCatalog = steps.Values.OrderBy(step => step.StepId).ToList(),
            TestSequenceVersions = sequences,
            DeploymentBindings = [],
            RetiredFunctionCodes = []
        };
    }

    private static RuleSetDefinition MapRuleSet(
        Legacy.TestItemDefinition item,
        IReadOnlyDictionary<Guid, Legacy.ModelBindingDefinition> legacyBindingLookup) => new()
        {
            LogicalOperator = item.RuleOperator == LegacyRules.RuleLogicalOperator.And
            ? RuleLogicalOperatorV2.And
            : RuleLogicalOperatorV2.Or,
            Rules = item.Rules.Select(rule => new InspectionRuleDefinition
            {
                RuleId = rule.Id,
                ModelBindingId = rule.ModelBindingId,
                OutputLabelId = legacyBindingLookup.TryGetValue(rule.ModelBindingId, out var binding)
                    ? binding.OutputLabelId
                    : 0,
                Metric = rule.Metric switch
                {
                    LegacyRules.QuantityMetric.MissingCount => RuleMetricV2.MissingCount,
                    LegacyRules.QuantityMetric.Presence => RuleMetricV2.Presence,
                    _ => RuleMetricV2.PresentCount
                },
                Operator = rule.Operator switch
                {
                    LegacyRules.ComparisonOperator.NotEqual => RuleComparisonOperatorV2.NotEqual,
                    LegacyRules.ComparisonOperator.GreaterThan => RuleComparisonOperatorV2.GreaterThan,
                    LegacyRules.ComparisonOperator.GreaterThanOrEqual => RuleComparisonOperatorV2.GreaterThanOrEqual,
                    LegacyRules.ComparisonOperator.LessThan => RuleComparisonOperatorV2.LessThan,
                    LegacyRules.ComparisonOperator.LessThanOrEqual => RuleComparisonOperatorV2.LessThanOrEqual,
                    LegacyRules.ComparisonOperator.BetweenInclusive => RuleComparisonOperatorV2.BetweenInclusive,
                    _ => RuleComparisonOperatorV2.Equal
                },
                Threshold = rule.Threshold,
                UpperThreshold = rule.UpperThreshold,
                ExpectedCount = rule.ExpectedCount,
                ConfidenceThreshold = rule.ConfidenceThreshold,
                Scope = new RegionScopeDefinitionV2
                {
                    Type = rule.Scope.Type == Legacy.RegionType.Roi ? RegionScopeTypeV2.Roi : RegionScopeTypeV2.FullImage,
                    Regions = rule.Scope.Regions.Select(region => new RegionOfInterestV2
                    {
                        RegionId = region.Id,
                        Name = region.Name,
                        X1 = region.X1,
                        Y1 = region.Y1,
                        X2 = region.X2,
                        Y2 = region.Y2,
                        ReferenceWidth = region.ReferenceWidth,
                        ReferenceHeight = region.ReferenceHeight
                    }).ToList()
                },
                OutcomeWhenMatched = rule.OutcomeWhenMatched == LegacyDomain.InspectionVerdict.Pass
                    ? RuleOutcome.Pass
                    : RuleOutcome.Fail
            }).ToList()
        };

    private static PoseProgramDefinition MapPoseProgram(Legacy.TestItemDefinition item) => new()
    {
        ProgramTimeoutMs = Math.Max(1, item.PoseSteps.Sum(action => action.MaximumWaitMs)),
        MaximumFrameGapMs = 500,
        Actions = item.PoseSteps.OrderBy(action => action.Order).Select(action => new PoseActionDefinition
        {
            ActionId = action.Id,
            Order = action.Order,
            Name = action.Name,
            ActionCondition = action.ActionCondition,
            ModelBindingId = action.ModelBindingId,
            ConfidenceThreshold = action.ConfidenceThreshold,
            MinimumHoldMs = action.MinimumHoldMs,
            MaximumWaitMs = action.MaximumWaitMs,
            IsRequired = action.IsRequired
        }).ToList()
    };

    private static TestStepKind MapTaskType(Legacy.ModelTaskType taskType) => taskType switch
    {
        Legacy.ModelTaskType.Classification => TestStepKind.Classification,
        Legacy.ModelTaskType.Segmentation => TestStepKind.Segmentation,
        Legacy.ModelTaskType.Pose => TestStepKind.Pose,
        Legacy.ModelTaskType.Temporal => TestStepKind.Temporal,
        _ => TestStepKind.Detection
    };

    private static string GetAdapterId(Legacy.ModelDefinition model)
    {
        if (model.Format != Legacy.ModelFormat.Onnx)
        {
            return $"unconfigured-{model.TaskType.ToString().ToLowerInvariant()}";
        }

        return model.TaskType switch
        {
            Legacy.ModelTaskType.Detection => KnownAdapterIds.YoloEndToEndDetection,
            Legacy.ModelTaskType.Classification => KnownAdapterIds.Classification,
            Legacy.ModelTaskType.Segmentation => KnownAdapterIds.Segmentation,
            Legacy.ModelTaskType.Pose => KnownAdapterIds.PoseKeypoint,
            Legacy.ModelTaskType.Temporal => KnownAdapterIds.TemporalAction,
            _ => "unconfigured-model"
        };
    }

    private static Guid DeriveGuid(Guid source, string discriminator)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{source:N}:{discriminator}"));
        return new Guid(bytes.AsSpan(0, 16));
    }

    private static string ComputeSequenceHash(TestSequenceVersion sequence)
    {
        var json = JsonSerializer.Serialize(sequence with { ContentHash = null });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }
}
