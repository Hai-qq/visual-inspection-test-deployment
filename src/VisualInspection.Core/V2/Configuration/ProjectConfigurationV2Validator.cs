using System.Text.RegularExpressions;

namespace VisualInspection.Core.V2.Configuration;

public static partial class ProjectConfigurationV2Validator
{
    public static IReadOnlyList<V2ValidationIssue> Validate(ProjectConfigurationV2 project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var issues = new List<V2ValidationIssue>();

        if (project.SchemaVersion != ConfigurationSchemaV2.CurrentVersion)
        {
            Error(issues, "V2-SCHEMA-001", "schemaVersion", $"配置必须使用 schema v{ConfigurationSchemaV2.CurrentVersion}。");
        }

        RequireId(project.ProjectId, "projectId", issues);
        RequireText(project.Name, "name", issues);
        RequireText(project.Workstation, "workstation", issues);
        CheckUniqueIds(project.ModelArtifacts, artifact => artifact.ModelArtifactId, "modelArtifacts", issues);
        CheckUniqueIds(project.RuntimeProfiles, profile => profile.RuntimeProfileId, "runtimeProfiles", issues);
        CheckUniqueIds(project.InputSourceDefinitions, source => source.InputSourceId, "inputSourceDefinitions", issues);
        CheckUniqueIds(project.TestStepCatalog, step => step.StepId, "testStepCatalog", issues);
        CheckUniqueIds(project.DeploymentBindings, binding => binding.DeploymentBindingId, "deploymentBindings", issues);

        ValidateFunctionCodes(project, issues);

        var profiles = UniqueLookup(project.RuntimeProfiles, value => value.RuntimeProfileId);
        var artifacts = UniqueLookup(project.ModelArtifacts, value => value.ModelArtifactId);
        var sources = UniqueLookup(project.InputSourceDefinitions, value => value.InputSourceId);
        var steps = UniqueLookup(project.TestStepCatalog, value => value.StepId);

        foreach (var profile in project.RuntimeProfiles)
        {
            ValidateRuntimeProfile(profile, issues);
        }

        foreach (var artifact in project.ModelArtifacts)
        {
            ValidateArtifact(artifact, profiles, issues);
        }

        foreach (var source in project.InputSourceDefinitions)
        {
            ValidateSource(source, issues);
        }

        foreach (var step in project.TestStepCatalog)
        {
            ValidateStep(step, artifacts, profiles, issues);
        }

        ValidateSequenceIdentity(project.TestSequenceVersions, issues);
        foreach (var sequence in project.TestSequenceVersions)
        {
            ValidateSequence(sequence, steps, sources, issues);
        }

        foreach (var deployment in project.DeploymentBindings)
        {
            ValidateDeployment(deployment, project, issues);
        }

        return issues;
    }

    public static bool HasErrors(IEnumerable<V2ValidationIssue> issues) =>
        issues.Any(issue => issue.Severity == V2ValidationSeverity.Error);

    private static void ValidateFunctionCodes(ProjectConfigurationV2 project, ICollection<V2ValidationIssue> issues)
    {
        foreach (var step in project.TestStepCatalog)
        {
            var path = $"testStepCatalog[{step.StepId}].functionCode";
            RequireText(step.FunctionCode, path, issues);
            if (!string.IsNullOrWhiteSpace(step.FunctionCode) && !FunctionCodePattern().IsMatch(step.FunctionCode))
            {
                Error(issues, "V2-FUNCTION-001", path, "FunctionCode 必须以字母开头，仅包含大写字母、数字、连字符或下划线，长度为 3–64。 ");
            }
        }

        foreach (var duplicate in project.TestStepCatalog
                     .Where(step => !string.IsNullOrWhiteSpace(step.FunctionCode))
                     .GroupBy(step => step.FunctionCode, StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1))
        {
            Error(issues, "V2-FUNCTION-002", "testStepCatalog", $"FunctionCode“{duplicate.Key}”在项目内重复。");
        }

        foreach (var duplicate in project.RetiredFunctionCodes
                     .Where(code => !string.IsNullOrWhiteSpace(code))
                     .GroupBy(code => code, StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1))
        {
            Error(issues, "V2-FUNCTION-003", "retiredFunctionCodes", $"已退役 FunctionCode“{duplicate.Key}”重复。");
        }

        var retired = project.RetiredFunctionCodes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var step in project.TestStepCatalog.Where(step => retired.Contains(step.FunctionCode)))
        {
            Error(issues, "V2-FUNCTION-004", $"testStepCatalog[{step.StepId}].functionCode", "FunctionCode 已退役，不能静默复用。");
        }
    }

    private static void ValidateRuntimeProfile(RuntimeProfile profile, ICollection<V2ValidationIssue> issues)
    {
        var path = $"runtimeProfiles[{profile.RuntimeProfileId}]";
        RequireId(profile.RuntimeProfileId, $"{path}.runtimeProfileId", issues);
        RequireText(profile.Name, $"{path}.name", issues);
        if (profile.IntraOpThreads <= 0 || profile.InterOpThreads <= 0 || profile.WarmupCount < 0 ||
            profile.MaxConcurrency <= 0 || profile.MemoryBudgetBytes <= 0)
        {
            Error(issues, "V2-RUNTIME-001", path, "线程数、最大并发和内存预算必须大于 0，预热次数不能为负数。");
        }

        if (profile.ExecutionProvider == RuntimeExecutionProvider.Cpu && profile.DeviceId is not null)
        {
            Warning(issues, "V2-RUNTIME-002", $"{path}.deviceId", "CPU Profile 会忽略 Device ID。");
        }
    }

    private static void ValidateArtifact(
        ModelArtifact artifact,
        IReadOnlyDictionary<Guid, RuntimeProfile> profiles,
        ICollection<V2ValidationIssue> issues)
    {
        var path = $"modelArtifacts[{artifact.ModelArtifactId}]";
        RequireId(artifact.ModelArtifactId, $"{path}.modelArtifactId", issues);
        RequireText(artifact.Name, $"{path}.name", issues);
        RequireText(artifact.Version, $"{path}.version", issues);
        RequireText(artifact.AdapterId, $"{path}.adapterId", issues);
        RequireText(artifact.LabelSetVersion, $"{path}.labelSetVersion", issues);
        if (string.IsNullOrWhiteSpace(artifact.FilePath) == string.IsNullOrWhiteSpace(artifact.ArtifactUri))
        {
            Error(issues, "V2-MODEL-001", path, "Model Artifact 必须且只能配置 FilePath 或 ArtifactUri 之一。");
        }

        if (!IsSha256(artifact.Sha256))
        {
            Error(issues, "V2-MODEL-002", $"{path}.sha256", "发布基础配置中的模型 SHA-256 必须是 64 位十六进制字符串。");
        }

        if (!profiles.ContainsKey(artifact.RuntimeProfileId))
        {
            Error(issues, "V2-MODEL-003", $"{path}.runtimeProfileId", "模型引用的 Runtime Profile 不存在。");
        }

        if (artifact.LabelSet.Count == 0)
        {
            Error(issues, "V2-MODEL-004", $"{path}.labelSet", "模型至少需要一个稳定 Label ID。");
        }

        foreach (var duplicate in artifact.LabelSet.GroupBy(label => label.Id).Where(group => group.Count() > 1))
        {
            Error(issues, "V2-MODEL-005", $"{path}.labelSet", $"Label ID {duplicate.Key} 重复。");
        }

        foreach (var label in artifact.LabelSet)
        {
            if (label.Id < 0)
            {
                Error(issues, "V2-MODEL-006", $"{path}.labelSet", "Label ID 不能为负数。");
            }

            RequireText(label.Name, $"{path}.labelSet[{label.Id}].name", issues);
        }

        if (artifact.Format == ModelArtifactFormat.Pt)
        {
            Warning(issues, "V2-MODEL-007", $"{path}.format", "PT 仅保留 schema；当前没有安全的 PT Runtime Adapter。");
        }
    }

    private static void ValidateSource(InputSourceDefinitionV2 source, ICollection<V2ValidationIssue> issues)
    {
        var path = $"inputSourceDefinitions[{source.InputSourceId}]";
        RequireId(source.InputSourceId, $"{path}.inputSourceId", issues);
        RequireId(source.SourceBindingId, $"{path}.sourceBindingId", issues);
        RequireText(source.Name, $"{path}.name", issues);
        if (source.MaximumFrameAgeMs <= 0)
        {
            Error(issues, "V2-SOURCE-001", $"{path}.maximumFrameAgeMs", "最大帧龄必须大于 0 毫秒。");
        }
    }

    private static void ValidateStep(
        TestStepDefinition step,
        IReadOnlyDictionary<Guid, ModelArtifact> artifacts,
        IReadOnlyDictionary<Guid, RuntimeProfile> profiles,
        ICollection<V2ValidationIssue> issues)
    {
        var path = $"testStepCatalog[{step.StepId}]";
        RequireId(step.StepId, $"{path}.stepId", issues);
        RequireText(step.Name, $"{path}.name", issues);
        if (step.DefaultTimeoutMs <= 0)
        {
            Error(issues, "V2-STEP-001", $"{path}.defaultTimeoutMs", "默认 Timeout 必须大于 0 毫秒。");
        }

        if (!step.InvocationPolicy.AllowSequenceInvocation &&
            !step.InvocationPolicy.AllowManualDebugInvocation &&
            step.InvocationPolicy.ExternalTriggerBindings.Count == 0)
        {
            Error(issues, "V2-TRIGGER-001", $"{path}.invocationPolicy", "测试步至少需要一种允许的调用入口。");
        }

        CheckUniqueIds(step.ModelBindings, binding => binding.ModelBindingId, $"{path}.modelBindings", issues);
        var bindings = UniqueLookup(step.ModelBindings, value => value.ModelBindingId);
        foreach (var binding in step.ModelBindings)
        {
            var bindingPath = $"{path}.modelBindings[{binding.ModelBindingId}]";
            RequireId(binding.ModelBindingId, $"{bindingPath}.modelBindingId", issues);
            RequireText(binding.ModelVersion, $"{bindingPath}.modelVersion", issues);
            RequireText(binding.AdapterId, $"{bindingPath}.adapterId", issues);
            if (!artifacts.TryGetValue(binding.ModelArtifactId, out var artifact))
            {
                Error(issues, "V2-BINDING-001", $"{bindingPath}.modelArtifactId", "模型绑定引用的 Model Artifact 不存在。");
                continue;
            }

            if (!string.Equals(binding.ModelVersion, artifact.Version, StringComparison.Ordinal))
            {
                Error(issues, "V2-BINDING-002", $"{bindingPath}.modelVersion", "绑定的模型版本与 Artifact 版本不一致。");
            }

            if (!string.Equals(binding.AdapterId, artifact.AdapterId, StringComparison.OrdinalIgnoreCase))
            {
                Error(issues, "V2-BINDING-003", $"{bindingPath}.adapterId", "绑定的 Adapter 与 Artifact Adapter 不一致。");
            }

            if (artifact.TaskType != step.Kind)
            {
                Error(
                    issues,
                    "V2-BINDING-006",
                    $"{bindingPath}.modelArtifactId",
                    $"模型任务类型 {artifact.TaskType} 与 Test Step Kind {step.Kind} 不一致。");
            }

            if (!profiles.ContainsKey(binding.AdapterProfileId) || binding.AdapterProfileId != artifact.RuntimeProfileId)
            {
                Error(issues, "V2-BINDING-004", $"{bindingPath}.adapterProfileId", "绑定引用的 Adapter Profile 不存在或与 Artifact 不一致。");
            }

            if (artifact.LabelSet.All(label => label.Id != binding.OutputLabelId))
            {
                Error(issues, "V2-BINDING-005", $"{bindingPath}.outputLabelId", "绑定引用的稳定 Label ID 不存在。");
            }
        }

        foreach (var external in step.InvocationPolicy.ExternalTriggerBindings)
        {
            ValidateExternalTrigger(external, $"{path}.invocationPolicy.externalTriggerBindings[{external.BindingId}]", issues);
        }

        CheckUniqueIds(
            step.InvocationPolicy.ExternalTriggerBindings,
            binding => binding.BindingId,
            $"{path}.invocationPolicy.externalTriggerBindings",
            issues);

        if (step.Kind is TestStepKind.Pose or TestStepKind.Temporal)
        {
            if (step.PoseProgram is null)
            {
                Error(issues, "V2-POSE-001", $"{path}.poseProgram", "Pose/Temporal Test Step 必须包含局部 Pose Program。");
            }

            if (step.RuleSet is not null)
            {
                Error(issues, "V2-POSE-002", $"{path}.ruleSet", "Pose/Temporal Test Step 不能同时包含普通 RuleSet。");
            }

            if (step.PoseProgram is not null)
            {
                ValidatePoseProgram(step.PoseProgram, bindings, $"{path}.poseProgram", issues);
            }
        }
        else
        {
            if (step.RuleSet is null)
            {
                Error(issues, "V2-RULE-001", $"{path}.ruleSet", "非 Pose Test Step 必须包含 RuleSet。");
            }

            if (step.PoseProgram is not null)
            {
                Error(issues, "V2-RULE-002", $"{path}.poseProgram", "非 Pose Test Step 不能包含 Pose Program。");
            }

            if (step.RuleSet is not null)
            {
                ValidateRuleSet(step.RuleSet, bindings, $"{path}.ruleSet", issues);
            }
        }
    }

    private static void ValidateExternalTrigger(
        ExternalTriggerBinding binding,
        string path,
        ICollection<V2ValidationIssue> issues)
    {
        RequireId(binding.BindingId, $"{path}.bindingId", issues);
        RequireText(binding.LogicalSignalTag, $"{path}.logicalSignalTag", issues);
        if (binding.DebounceMs < 0 || binding.PostTriggerDelayMs < 0 || binding.TimeoutMs <= 0 ||
            binding.MaxConcurrency <= 0 || binding.QueueCapacity <= 0)
        {
            Error(issues, "V2-TRIGGER-002", path, "去抖和触发后延时不能为负；Timeout、最大并发和队列容量必须大于 0。");
        }
    }

    private static void ValidateRuleSet(
        RuleSetDefinition ruleSet,
        IReadOnlyDictionary<Guid, ModelBindingV2> bindings,
        string path,
        ICollection<V2ValidationIssue> issues)
    {
        if (ruleSet.Rules.Count == 0)
        {
            Error(issues, "V2-RULE-003", $"{path}.rules", "RuleSet 至少需要一条规则。");
        }

        CheckUniqueIds(ruleSet.Rules, rule => rule.RuleId, $"{path}.rules", issues);
        foreach (var rule in ruleSet.Rules)
        {
            var rulePath = $"{path}.rules[{rule.RuleId}]";
            RequireId(rule.RuleId, $"{rulePath}.ruleId", issues);
            if (!bindings.TryGetValue(rule.ModelBindingId, out var binding))
            {
                Error(issues, "V2-RULE-004", $"{rulePath}.modelBindingId", "规则引用的模型绑定不属于当前 Test Step。");
            }
            else if (binding.OutputLabelId != rule.OutputLabelId)
            {
                Error(issues, "V2-RULE-005", $"{rulePath}.outputLabelId", "规则 Label ID 与模型绑定不一致。");
            }

            if (rule.ConfidenceThreshold is < 0 or > 1)
            {
                Error(issues, "V2-RULE-006", $"{rulePath}.confidenceThreshold", "Confidence 必须位于 0 到 1 之间。");
            }

            if (rule.Threshold < 0 || rule.ExpectedCount < 0)
            {
                Error(issues, "V2-RULE-007", rulePath, "数量阈值不能为负数。");
            }

            if (rule.Metric == RuleMetricV2.MissingCount && rule.ExpectedCount is null)
            {
                Error(issues, "V2-RULE-008", $"{rulePath}.expectedCount", "MissingCount 必须配置 ExpectedCount。");
            }

            if (rule.Operator == RuleComparisonOperatorV2.BetweenInclusive &&
                (rule.UpperThreshold is null || rule.UpperThreshold < rule.Threshold))
            {
                Error(issues, "V2-RULE-009", $"{rulePath}.upperThreshold", "BetweenInclusive 必须配置不小于下限的上限。");
            }

            ValidateScope(rule.Scope, $"{rulePath}.scope", issues);
        }
    }

    private static void ValidateScope(
        RegionScopeDefinitionV2 scope,
        string path,
        ICollection<V2ValidationIssue> issues)
    {
        if (scope.Type == RegionScopeTypeV2.FullImage)
        {
            if (scope.Regions.Count != 0)
            {
                Error(issues, "V2-ROI-001", $"{path}.regions", "Full Image 不能携带 ROI。");
            }

            return;
        }

        if (scope.Regions.Count == 0)
        {
            Error(issues, "V2-ROI-002", $"{path}.regions", "ROI 范围至少需要一个矩形。");
        }

        CheckUniqueIds(scope.Regions, region => region.RegionId, $"{path}.regions", issues);
        foreach (var region in scope.Regions)
        {
            var regionPath = $"{path}.regions[{region.RegionId}]";
            RequireId(region.RegionId, $"{regionPath}.regionId", issues);
            RequireText(region.Name, $"{regionPath}.name", issues);
            if (region.ReferenceWidth <= 0 || region.ReferenceHeight <= 0 || region.X1 < 0 || region.Y1 < 0 ||
                region.X1 >= region.X2 || region.Y1 >= region.Y2 || region.X2 > region.ReferenceWidth ||
                region.Y2 > region.ReferenceHeight)
            {
                Error(issues, "V2-ROI-003", regionPath, "ROI 必须位于有效参考图像范围内并满足 x1 < x2、y1 < y2。");
            }
        }
    }

    private static void ValidatePoseProgram(
        PoseProgramDefinition program,
        IReadOnlyDictionary<Guid, ModelBindingV2> bindings,
        string path,
        ICollection<V2ValidationIssue> issues)
    {
        if (program.ProgramTimeoutMs <= 0 || program.MaximumFrameGapMs <= 0)
        {
            Error(issues, "V2-POSE-003", path, "Pose Program Timeout 和最大帧间隔必须大于 0。");
        }

        if (program.Actions.Count == 0)
        {
            Error(issues, "V2-POSE-004", $"{path}.actions", "Pose Program 至少需要一个动作。");
        }

        CheckUniqueIds(program.Actions, action => action.ActionId, $"{path}.actions", issues);
        CheckContiguousOrders(program.Actions.Select(action => action.Order), $"{path}.actions", issues);
        foreach (var action in program.Actions)
        {
            var actionPath = $"{path}.actions[{action.ActionId}]";
            RequireId(action.ActionId, $"{actionPath}.actionId", issues);
            RequireText(action.Name, $"{actionPath}.name", issues);
            RequireText(action.ActionCondition, $"{actionPath}.actionCondition", issues);
            if (!bindings.ContainsKey(action.ModelBindingId))
            {
                Error(issues, "V2-POSE-005", $"{actionPath}.modelBindingId", "动作引用的模型绑定不属于当前 Test Step。");
            }

            if (action.ConfidenceThreshold is < 0 or > 1 || action.MinimumHoldMs < 0 ||
                action.MaximumWaitMs <= 0 || action.MinimumHoldMs > action.MaximumWaitMs)
            {
                Error(issues, "V2-POSE-006", actionPath, "动作 Confidence、Hold 或 Wait 参数无效。");
            }
        }
    }

    private static void ValidateSequence(
        TestSequenceVersion sequence,
        IReadOnlyDictionary<Guid, TestStepDefinition> steps,
        IReadOnlyDictionary<Guid, InputSourceDefinitionV2> sources,
        ICollection<V2ValidationIssue> issues)
    {
        var path = $"testSequenceVersions[{sequence.SequenceId}:{sequence.Version}]";
        RequireId(sequence.SequenceId, $"{path}.sequenceId", issues);
        RequireText(sequence.Name, $"{path}.name", issues);
        RequireText(sequence.Version, $"{path}.version", issues);
        if (sequence.OrderedInvocations.Count == 0)
        {
            Error(issues, "V2-SEQUENCE-001", $"{path}.orderedInvocations", "Test Sequence 至少需要一个有序 Invocation。");
        }

        if (sequence.DefaultSourceBindingId == Guid.Empty ||
            sources.Values.All(source => source.SourceBindingId != sequence.DefaultSourceBindingId))
        {
            Error(issues, "V2-SEQUENCE-002", $"{path}.defaultSourceBindingId", "默认 Source Binding 不存在。");
        }

        CheckUniqueIds(sequence.OrderedInvocations, invocation => invocation.InvocationId, $"{path}.orderedInvocations", issues);
        CheckContiguousOrders(sequence.OrderedInvocations.Select(invocation => invocation.Order), $"{path}.orderedInvocations", issues);
        foreach (var invocation in sequence.OrderedInvocations)
        {
            var invocationPath = $"{path}.orderedInvocations[{invocation.InvocationId}]";
            RequireId(invocation.InvocationId, $"{invocationPath}.invocationId", issues);
            if (!steps.TryGetValue(invocation.StepId, out var step))
            {
                Error(issues, "V2-SEQUENCE-003", $"{invocationPath}.stepId", "Invocation 引用的 Test Step 不存在。");
            }
            else if (!step.InvocationPolicy.AllowSequenceInvocation)
            {
                Error(issues, "V2-SEQUENCE-004", $"{invocationPath}.stepId", "Test Step 禁止由 Sequence 调用。");
            }

            if (invocation.DelayMs < 0 || invocation.TimeoutOverrideMs <= 0)
            {
                Error(issues, "V2-SEQUENCE-005", invocationPath, "Delay 不能为负；Timeout Override 配置后必须大于 0。");
            }

            var sourceBindingId = invocation.SourceBindingId ?? sequence.DefaultSourceBindingId;
            if (sources.Values.All(source => source.SourceBindingId != sourceBindingId))
            {
                Error(issues, "V2-SEQUENCE-006", $"{invocationPath}.sourceBindingId", "Invocation 引用的 Source Binding 不存在。");
            }
        }

        if (sequence.IsPublished &&
            (sequence.PublishedAtUtc is null || !IsSha256(sequence.ContentHash)))
        {
            Error(issues, "V2-SEQUENCE-007", path, "已发布 Sequence Version 必须包含 PublishedAtUtc 和 ContentHash。");
        }
    }

    private static void ValidateSequenceIdentity(
        IEnumerable<TestSequenceVersion> sequences,
        ICollection<V2ValidationIssue> issues)
    {
        foreach (var duplicate in sequences.GroupBy(sequence => (sequence.SequenceId, sequence.Version)).Where(group => group.Count() > 1))
        {
            Error(issues, "V2-SEQUENCE-008", "testSequenceVersions", $"SequenceId 与 Version 重复：{duplicate.Key.SequenceId} {duplicate.Key.Version}。");
        }
    }

    private static void ValidateDeployment(
        DeploymentBinding deployment,
        ProjectConfigurationV2 project,
        ICollection<V2ValidationIssue> issues)
    {
        var path = $"deploymentBindings[{deployment.DeploymentBindingId}]";
        RequireId(deployment.DeploymentBindingId, $"{path}.deploymentBindingId", issues);
        RequireText(deployment.Name, $"{path}.name", issues);
        RequireText(deployment.StationId, $"{path}.stationId", issues);
        if (deployment.LineResultBinding is null)
        {
            Error(issues, "V2-DEPLOY-001", $"{path}.lineResultBinding", "Deployment 必须配置 Line Result Binding。");
        }
        else
        {
            RequireText(deployment.LineResultBinding.AdapterId, $"{path}.lineResultBinding.adapterId", issues);
            RequireText(deployment.LineResultBinding.ConnectionProfileId, $"{path}.lineResultBinding.connectionProfileId", issues);
        }

        foreach (var duplicate in deployment.TriggerSignalBindings
                     .GroupBy(binding => binding.LogicalSignalTag, StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1))
        {
            Error(issues, "V2-DEPLOY-002", $"{path}.triggerSignalBindings", $"Signal Tag“{duplicate.Key}”映射重复。");
        }

        var usedSignalTags = project.TestStepCatalog
            .SelectMany(step => step.InvocationPolicy.ExternalTriggerBindings)
            .Select(binding => binding.LogicalSignalTag)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var mappedSignalTags = deployment.TriggerSignalBindings
            .Select(binding => binding.LogicalSignalTag)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in usedSignalTags.Except(mappedSignalTags, StringComparer.OrdinalIgnoreCase))
        {
            Error(issues, "V2-DEPLOY-003", $"{path}.triggerSignalBindings", $"外部 Signal Tag“{tag}”没有现场地址映射。");
        }

        var sourceIds = project.InputSourceDefinitions.Select(source => source.InputSourceId).ToHashSet();
        var sourceBindingIds = project.InputSourceDefinitions.Select(source => source.SourceBindingId).ToHashSet();
        foreach (var binding in deployment.InputSourceBindings)
        {
            if (!sourceIds.Contains(binding.InputSourceId) || !sourceBindingIds.Contains(binding.SourceBindingId))
            {
                Error(issues, "V2-DEPLOY-004", $"{path}.inputSourceBindings", "Input Source Deployment Binding 引用了未知源。");
            }

            RequireText(binding.CameraAdapterId, $"{path}.inputSourceBindings[{binding.SourceBindingId}].cameraAdapterId", issues);
        }
    }

    private static void CheckContiguousOrders(
        IEnumerable<int> values,
        string path,
        ICollection<V2ValidationIssue> issues)
    {
        var orders = values.Order().ToArray();
        if (orders.Length == 0)
        {
            return;
        }

        if (!orders.SequenceEqual(Enumerable.Range(1, orders.Length)))
        {
            Error(issues, "V2-ORDER-001", path, "Order 必须唯一且连续，从 1 开始。");
        }
    }

    private static IReadOnlyDictionary<Guid, T> UniqueLookup<T>(IEnumerable<T> values, Func<T, Guid> getId) =>
        values.GroupBy(getId).ToDictionary(group => group.Key, group => group.First());

    private static void CheckUniqueIds<T>(
        IEnumerable<T> values,
        Func<T, Guid> getId,
        string path,
        ICollection<V2ValidationIssue> issues)
    {
        foreach (var duplicate in values.GroupBy(getId).Where(group => group.Count() > 1))
        {
            Error(issues, "V2-ID-001", path, $"ID {duplicate.Key} 重复。");
        }
    }

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static void RequireId(Guid value, string path, ICollection<V2ValidationIssue> issues)
    {
        if (value == Guid.Empty)
        {
            Error(issues, "V2-REQUIRED-001", path, "ID 不能为空。");
        }
    }

    private static void RequireText(string? value, string path, ICollection<V2ValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            Error(issues, "V2-REQUIRED-002", path, "文本值不能为空。");
        }
    }

    private static void Error(ICollection<V2ValidationIssue> issues, string code, string path, string message) =>
        issues.Add(new V2ValidationIssue(V2ValidationSeverity.Error, code, path, message));

    private static void Warning(ICollection<V2ValidationIssue> issues, string code, string path, string message) =>
        issues.Add(new V2ValidationIssue(V2ValidationSeverity.Warning, code, path, message));

    [GeneratedRegex("^[A-Z][A-Z0-9_-]{2,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex FunctionCodePattern();
}
