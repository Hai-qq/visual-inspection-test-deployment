using VisualInspection.Core.Configuration;
using VisualInspection.Core.Domain;
using VisualInspection.Core.Rules;

namespace VisualInspection.Core.V2.Configuration;

/// <summary>
/// Converts the production-supported V2 portable sequence subset into the v1 execution
/// contract currently consumed by the WPF operator workbench.
/// </summary>
public static class ProjectConfigurationV2CompatibilityConverter
{
    public static ProjectConfiguration ToV1(ProjectConfigurationV2 project, string sequenceDirectory)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(sequenceDirectory);

        var schemaErrors = ProjectConfigurationV2Validator.Validate(project)
            .Where(issue => issue.Severity == V2ValidationSeverity.Error)
            .ToArray();
        if (schemaErrors.Length > 0)
        {
            throw new InvalidDataException(
                "sequence 配置校验失败：" +
                string.Join("；", schemaErrors.Select(issue => $"{issue.Code} {issue.Message}")));
        }

        var sequence = project.TestSequenceVersions.SingleOrDefault()
            ?? throw new InvalidDataException("可移植 sequence 必须且只能包含一个型号。");
        var source = project.InputSourceDefinitions.SingleOrDefault(candidate =>
                candidate.SourceBindingId == sequence.DefaultSourceBindingId)
            ?? throw new InvalidDataException("sequence 未找到默认图源定义。");
        var deploymentSource = project.DeploymentBindings
            .SelectMany(binding => binding.InputSourceBindings)
            .FirstOrDefault(binding => binding.SourceBindingId == source.SourceBindingId);

        var models = project.ModelArtifacts.Select(model => ConvertModel(model, sequenceDirectory)).ToList();
        var modelById = models.ToDictionary(model => model.Id);
        var artifactById = project.ModelArtifacts.ToDictionary(model => model.ModelArtifactId);
        var stepsById = project.TestStepCatalog.ToDictionary(step => step.StepId);
        var targets = new List<TargetDefinition>();
        var items = new List<TestItemDefinition>();

        foreach (var invocation in sequence.OrderedInvocations.OrderBy(value => value.Order))
        {
            if (!stepsById.TryGetValue(invocation.StepId, out var step))
            {
                throw new InvalidDataException($"sequence 引用了不存在的测试步：{invocation.StepId}。");
            }

            if (step.Kind != TestStepKind.Detection || step.RuleSet is null)
            {
                throw new NotSupportedException(
                    $"当前操作端只支持 ONNX Detection sequence；测试步“{step.Name}”为 {step.Kind}。");
            }

            var bindingsById = step.ModelBindings.ToDictionary(binding => binding.ModelBindingId);
            var rules = new List<TargetRuleDefinition>();
            foreach (var rule in step.RuleSet.Rules)
            {
                if (!bindingsById.TryGetValue(rule.ModelBindingId, out var binding) ||
                    !artifactById.TryGetValue(binding.ModelArtifactId, out var artifact) ||
                    !modelById.ContainsKey(binding.ModelArtifactId))
                {
                    throw new InvalidDataException($"测试步“{step.Name}”的规则模型绑定无效。");
                }

                var label = artifact.LabelSet.FirstOrDefault(candidate => candidate.Id == rule.OutputLabelId)
                    ?? throw new InvalidDataException(
                        $"模型“{artifact.Name}”不存在 Label ID {rule.OutputLabelId}。");
                var targetId = rule.RuleId;
                targets.Add(new TargetDefinition
                {
                    Id = targetId,
                    Name = label.Name,
                    ModelBindings =
                    [
                        new ModelBindingDefinition
                        {
                            Id = binding.ModelBindingId,
                            ModelId = binding.ModelArtifactId,
                            ModelVersion = binding.ModelVersion,
                            OutputLabelId = binding.OutputLabelId
                        }
                    ]
                });
                rules.Add(new TargetRuleDefinition
                {
                    Id = rule.RuleId,
                    TargetId = targetId,
                    ModelBindingId = binding.ModelBindingId,
                    Scope = ConvertScope(rule.Scope),
                    Metric = rule.Metric switch
                    {
                        RuleMetricV2.MissingCount => QuantityMetric.MissingCount,
                        RuleMetricV2.Presence => QuantityMetric.Presence,
                        _ => QuantityMetric.PresentCount
                    },
                    Operator = rule.Operator switch
                    {
                        RuleComparisonOperatorV2.NotEqual => ComparisonOperator.NotEqual,
                        RuleComparisonOperatorV2.GreaterThan => ComparisonOperator.GreaterThan,
                        RuleComparisonOperatorV2.GreaterThanOrEqual => ComparisonOperator.GreaterThanOrEqual,
                        RuleComparisonOperatorV2.LessThan => ComparisonOperator.LessThan,
                        RuleComparisonOperatorV2.LessThanOrEqual => ComparisonOperator.LessThanOrEqual,
                        RuleComparisonOperatorV2.BetweenInclusive => ComparisonOperator.BetweenInclusive,
                        _ => ComparisonOperator.Equal
                    },
                    Threshold = rule.Threshold,
                    UpperThreshold = rule.UpperThreshold,
                    ExpectedCount = rule.ExpectedCount,
                    ConfidenceThreshold = rule.ConfidenceThreshold,
                    OutcomeWhenMatched = rule.OutcomeWhenMatched == RuleOutcome.Fail
                        ? InspectionVerdict.Fail
                        : InspectionVerdict.Pass
                });
            }

            items.Add(new TestItemDefinition
            {
                Id = step.StepId,
                Order = invocation.Order,
                Name = step.Name,
                Type = TestItemType.Normal,
                Enabled = true,
                IsRequired = invocation.IsRequired,
                DelayMs = invocation.DelayMs,
                RuleOperator = step.RuleSet.LogicalOperator == RuleLogicalOperatorV2.Or
                    ? RuleLogicalOperator.Or
                    : RuleLogicalOperator.And,
                Rules = rules
            });
        }

        var sourceDefinition = ConvertSource(source, deploymentSource, sequenceDirectory);
        return new ProjectConfiguration
        {
            Id = project.ProjectId,
            Name = project.Name,
            Workstation = project.Workstation,
            Models = models,
            Targets = targets,
            InputSources = [sourceDefinition],
            TestSequences =
            [
                new TestSequenceDefinition
                {
                    Id = sequence.SequenceId,
                    Name = sequence.Name,
                    Version = sequence.Version,
                    DefaultDelayMs = 0,
                    InputSourceId = sourceDefinition.Id,
                    SourcePolicy = RuntimeSourcePolicy.Fixed,
                    IsPublished = true,
                    PublishedAtUtc = sequence.PublishedAtUtc ?? DateTimeOffset.UtcNow,
                    Items = items
                }
            ]
        };
    }

    private static ModelDefinition ConvertModel(ModelArtifact model, string sequenceDirectory)
    {
        if (model.Format != ModelArtifactFormat.Onnx ||
            model.TaskType != TestStepKind.Detection ||
            !string.Equals(model.AdapterId, KnownAdapterIds.YoloEndToEndDetection, StringComparison.Ordinal))
        {
            throw new NotSupportedException(
                $"当前操作端只支持 onnx-yolo-e2e-detection；模型“{model.Name}”不在支持范围内。");
        }

        var configuredPath = model.FilePath ?? model.ArtifactUri;
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            throw new InvalidDataException($"模型“{model.Name}”没有文件路径。");
        }

        var resolvedPath = Path.IsPathRooted(configuredPath)
            ? Path.GetFullPath(configuredPath)
            : Path.GetFullPath(Path.Combine(sequenceDirectory, configuredPath));
        return new ModelDefinition
        {
            Id = model.ModelArtifactId,
            Name = model.Name,
            Version = model.Version,
            Format = ModelFormat.Onnx,
            TaskType = ModelTaskType.Detection,
            FilePath = resolvedPath,
            Sha256 = model.Sha256,
            LabelSource = LabelSourceMode.ImportedFromModel,
            Labels = model.LabelSet.OrderBy(label => label.Id).Select(label => new ModelLabelDefinition
            {
                Id = label.Id,
                Name = label.Name
            }).ToList()
        };
    }

    private static InputSourceDefinition ConvertSource(
        InputSourceDefinitionV2 source,
        InputSourceDeploymentBinding? deployment,
        string sequenceDirectory)
    {
        var address = deployment?.DeviceAddress ?? string.Empty;
        if (source.Kind == InputSourceKind.Folder)
        {
            if (string.IsNullOrWhiteSpace(address))
            {
                address = "input";
            }

            return new InputSourceDefinition
            {
                Id = source.InputSourceId,
                Name = source.Name,
                Type = InputSourceType.Folder,
                Folder = new FolderInputOptions
                {
                    FolderPath = string.IsNullOrWhiteSpace(address) || Path.IsPathRooted(address)
                        ? address
                        : Path.GetFullPath(Path.Combine(sequenceDirectory, address)),
                    IncludeSubfolders = false,
                    SortOrder = FolderSortOrder.NaturalFileName,
                    InvalidFileBehavior = InvalidFileBehavior.Stop,
                    LoopPlayback = true,
                    PoseFrameIntervalMs = 33
                }
            };
        }

        if (source.Kind != InputSourceKind.Camera)
        {
            throw new NotSupportedException($"当前操作端不支持图源类型 {source.Kind}。");
        }

        var isUsb = source.Name.Contains("USB", StringComparison.OrdinalIgnoreCase);
        return new InputSourceDefinition
        {
            Id = source.InputSourceId,
            Name = source.Name,
            Type = isUsb ? InputSourceType.DirectShowCamera : InputSourceType.VendorCamera,
            Camera = new CameraInputOptions
            {
                AdapterId = deployment?.CameraAdapterId ?? KnownAdapterIds.UnconfiguredCamera,
                DeviceId = address,
                Width = 1920,
                Height = 1080,
                FrameRate = 30,
                PixelFormat = "BGR24",
                TriggerMode = "Software",
                GrabTimeoutMs = 1000
            }
        };
    }

    private static RegionScopeDefinition ConvertScope(RegionScopeDefinitionV2 scope) => new()
    {
        Type = scope.Type == RegionScopeTypeV2.Roi ? RegionType.Roi : RegionType.FullImage,
        Regions = scope.Regions.Select(region => new RegionOfInterestDefinition
        {
            Id = region.RegionId,
            Name = region.Name,
            X1 = region.X1,
            Y1 = region.Y1,
            X2 = region.X2,
            Y2 = region.Y2,
            ReferenceWidth = region.ReferenceWidth,
            ReferenceHeight = region.ReferenceHeight
        }).ToList()
    };
}
