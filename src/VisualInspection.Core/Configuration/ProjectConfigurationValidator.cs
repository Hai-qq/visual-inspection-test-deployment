using VisualInspection.Core.Domain;
using VisualInspection.Core.Rules;

namespace VisualInspection.Core.Configuration;

public static class ProjectConfigurationValidator
{
    public static IReadOnlyList<ConfigurationValidationIssue> Validate(ProjectConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var issues = new List<ConfigurationValidationIssue>();

        RequireId(configuration.Id, "project.id", issues);
        RequireText(configuration.Name, "project.name", issues);
        CheckUniqueIds(configuration.Models, model => model.Id, "models", issues);
        CheckUniqueIds(configuration.Targets, target => target.Id, "targets", issues);
        CheckUniqueIds(configuration.InputSources, source => source.Id, "inputSources", issues);
        CheckUniqueIds(configuration.TestSequences, sequence => sequence.Id, "testSequences", issues);

        var models = configuration.Models.GroupBy(model => model.Id).ToDictionary(group => group.Key, group => group.First());
        var targets = configuration.Targets.GroupBy(target => target.Id).ToDictionary(group => group.Key, group => group.First());
        var bindings = configuration.Targets
            .SelectMany(target => target.ModelBindings.Select(binding => (target, binding)))
            .GroupBy(pair => pair.binding.Id)
            .ToDictionary(group => group.Key, group => group.First());

        foreach (var model in configuration.Models)
        {
            ValidateModel(model, issues);
        }

        CheckUniqueIds(configuration.Targets.SelectMany(target => target.ModelBindings), binding => binding.Id, "modelBindings", issues);
        foreach (var target in configuration.Targets)
        {
            ValidateTarget(target, models, issues);
        }

        foreach (var source in configuration.InputSources)
        {
            ValidateInputSource(source, issues);
        }

        var sourceIds = configuration.InputSources.Select(source => source.Id).ToHashSet();
        foreach (var duplicate in configuration.TestSequences
                     .Where(sequence => sequence.IsPublished)
                     .GroupBy(sequence => (sequence.Name, sequence.Version))
                     .Where(group => group.Count() > 1))
        {
            AddError(
                issues,
                "CFG-SEQ-005",
                "testSequences",
                $"已发布测试序列的名称与版本必须唯一：{duplicate.Key.Name} {duplicate.Key.Version}。");
        }

        foreach (var sequence in configuration.TestSequences)
        {
            ValidateSequence(sequence, sourceIds, targets, bindings, models, issues);
        }

        return issues;
    }

    public static bool HasErrors(IEnumerable<ConfigurationValidationIssue> issues) =>
        issues.Any(issue => issue.Severity == ConfigurationValidationSeverity.Error);

    private static void ValidateModel(ModelDefinition model, List<ConfigurationValidationIssue> issues)
    {
        var path = $"models[{model.Id}]";
        RequireId(model.Id, $"{path}.id", issues);
        RequireText(model.Name, $"{path}.name", issues);
        RequireText(model.Version, $"{path}.version", issues);
        RequireText(model.FilePath, $"{path}.filePath", issues);

        var duplicateLabelIds = model.Labels.GroupBy(label => label.Id).Where(group => group.Count() > 1);
        foreach (var duplicate in duplicateLabelIds)
        {
            AddError(issues, "CFG-MODEL-001", $"{path}.labels", $"标签标识 {duplicate.Key} 重复。");
        }

        foreach (var label in model.Labels)
        {
            if (label.Id < 0)
            {
                AddError(issues, "CFG-MODEL-002", $"{path}.labels", "标签标识不能为负数。");
            }

            RequireText(label.Name, $"{path}.labels[{label.Id}].name", issues);
        }

        if (string.IsNullOrWhiteSpace(model.Sha256))
        {
            issues.Add(new ConfigurationValidationIssue(
                ConfigurationValidationSeverity.Warning,
                "CFG-MODEL-003",
                $"{path}.sha256",
                "模型尚未记录 SHA-256，发布前应在文件可用后补齐。"));
        }
        else if (model.Sha256.Length != 64 || model.Sha256.Any(character => !Uri.IsHexDigit(character)))
        {
            AddError(issues, "CFG-MODEL-004", $"{path}.sha256", "SHA-256 必须是 64 位十六进制字符串。");
        }
    }

    private static void ValidateTarget(
        TargetDefinition target,
        IReadOnlyDictionary<Guid, ModelDefinition> models,
        List<ConfigurationValidationIssue> issues)
    {
        var path = $"targets[{target.Id}]";
        RequireId(target.Id, $"{path}.id", issues);
        RequireText(target.Name, $"{path}.name", issues);
        if (target.ModelBindings.Count == 0)
        {
            AddError(issues, "CFG-BIND-001", $"{path}.modelBindings", "目标至少需要一个显式模型绑定。");
        }

        foreach (var binding in target.ModelBindings)
        {
            var bindingPath = $"{path}.modelBindings[{binding.Id}]";
            RequireId(binding.Id, $"{bindingPath}.id", issues);
            if (!models.TryGetValue(binding.ModelId, out var model))
            {
                AddError(issues, "CFG-BIND-002", $"{bindingPath}.modelId", "绑定引用的模型不存在。");
                continue;
            }

            if (!string.Equals(binding.ModelVersion, model.Version, StringComparison.Ordinal))
            {
                AddError(issues, "CFG-BIND-003", $"{bindingPath}.modelVersion", "绑定的模型版本与模型记录不一致。");
            }

            if (model.Labels.All(label => label.Id != binding.OutputLabelId))
            {
                AddError(issues, "CFG-BIND-004", $"{bindingPath}.outputLabelId", "绑定引用的输出标签不存在。");
            }
        }
    }

    private static void ValidateInputSource(InputSourceDefinition source, List<ConfigurationValidationIssue> issues)
    {
        var path = $"inputSources[{source.Id}]";
        RequireId(source.Id, $"{path}.id", issues);
        RequireText(source.Name, $"{path}.name", issues);

        if (source.Type == InputSourceType.Folder)
        {
            if (source.Folder is null)
            {
                AddError(issues, "CFG-SOURCE-001", $"{path}.folder", "文件夹图源缺少文件夹配置。");
                return;
            }

            RequireText(source.Folder.FolderPath, $"{path}.folder.folderPath", issues);
            if (source.Folder.PoseFrameIntervalMs <= 0)
            {
                AddError(issues, "CFG-SOURCE-002", $"{path}.folder.poseFrameIntervalMs", "姿态帧间隔必须大于 0 毫秒。");
            }

            if (source.Camera is not null)
            {
                AddError(issues, "CFG-SOURCE-003", $"{path}.camera", "文件夹图源不能同时包含相机配置。");
            }

            return;
        }

        if (source.Camera is null)
        {
            AddError(issues, "CFG-SOURCE-004", $"{path}.camera", "相机图源缺少相机配置。");
            return;
        }

        RequireText(source.Camera.AdapterId, $"{path}.camera.adapterId", issues);
        RequireText(source.Camera.DeviceId, $"{path}.camera.deviceId", issues);
        if (source.Camera.Width <= 0 || source.Camera.Height <= 0 || source.Camera.FrameRate <= 0)
        {
            AddError(issues, "CFG-SOURCE-005", $"{path}.camera", "相机宽、高和帧率必须大于 0。");
        }

        if (source.Camera.GrabTimeoutMs <= 0)
        {
            AddError(issues, "CFG-SOURCE-006", $"{path}.camera.grabTimeoutMs", "取帧超时必须大于 0 毫秒。");
        }

        if (source.Folder is not null)
        {
            AddError(issues, "CFG-SOURCE-007", $"{path}.folder", "相机图源不能同时包含文件夹配置。");
        }
    }

    private static void ValidateSequence(
        TestSequenceDefinition sequence,
        IReadOnlySet<Guid> sourceIds,
        IReadOnlyDictionary<Guid, TargetDefinition> targets,
        IReadOnlyDictionary<Guid, (TargetDefinition target, ModelBindingDefinition binding)> bindings,
        IReadOnlyDictionary<Guid, ModelDefinition> models,
        List<ConfigurationValidationIssue> issues)
    {
        var path = $"testSequences[{sequence.Id}]";
        RequireId(sequence.Id, $"{path}.id", issues);
        RequireText(sequence.Name, $"{path}.name", issues);
        RequireText(sequence.Version, $"{path}.version", issues);

        if (sequence.DefaultDelayMs < 0)
        {
            AddError(issues, "CFG-SEQ-001", $"{path}.defaultDelayMs", "默认延时不能为负数。");
        }

        if (!sourceIds.Contains(sequence.InputSourceId))
        {
            AddError(issues, "CFG-SEQ-002", $"{path}.inputSourceId", "测试序列引用的图源不存在。");
        }

        if (sequence.IsPublished && sequence.PublishedAtUtc is null)
        {
            AddError(issues, "CFG-SEQ-003", $"{path}.publishedAtUtc", "已发布版本必须记录发布时间。");
        }

        if (sequence.Items.Count == 0)
        {
            AddError(issues, "CFG-SEQ-004", $"{path}.items", "测试序列至少需要一个测试项。");
        }

        CheckUniqueIds(sequence.Items, item => item.Id, $"{path}.items", issues);
        CheckPositiveUniqueOrders(sequence.Items.Select(item => item.Order), $"{path}.items", issues);

        foreach (var item in sequence.Items)
        {
            ValidateItem(item, targets, bindings, models, $"{path}.items[{item.Id}]", issues);
        }

        if (sequence.IsPublished)
        {
            var usedBindingIds = sequence.Items
                .SelectMany(item => item.Rules.Select(rule => rule.ModelBindingId)
                    .Concat(item.PoseSteps.Select(step => step.ModelBindingId)))
                .Distinct();
            foreach (var bindingId in usedBindingIds)
            {
                if (bindings.TryGetValue(bindingId, out var pair) &&
                    models.TryGetValue(pair.binding.ModelId, out var model) &&
                    string.IsNullOrWhiteSpace(model.Sha256))
                {
                    AddError(
                        issues,
                        "CFG-SEQ-006",
                        $"{path}.items",
                        $"已发布序列引用的模型 {model.Name} 缺少 SHA-256。");
                }
            }
        }
    }

    private static void ValidateItem(
        TestItemDefinition item,
        IReadOnlyDictionary<Guid, TargetDefinition> targets,
        IReadOnlyDictionary<Guid, (TargetDefinition target, ModelBindingDefinition binding)> bindings,
        IReadOnlyDictionary<Guid, ModelDefinition> models,
        string path,
        List<ConfigurationValidationIssue> issues)
    {
        RequireId(item.Id, $"{path}.id", issues);
        RequireText(item.Name, $"{path}.name", issues);
        if (item.DelayMs < 0)
        {
            AddError(issues, "CFG-ITEM-001", $"{path}.delayMs", "测试项延时不能为负数。");
        }

        if (item.Type == TestItemType.Normal)
        {
            if (item.Rules.Count == 0)
            {
                AddError(issues, "CFG-ITEM-002", $"{path}.rules", "普通视觉测试项至少需要一条规则。");
            }

            if (item.PoseSteps.Count > 0)
            {
                AddError(issues, "CFG-ITEM-003", $"{path}.poseSteps", "普通视觉测试项不能包含姿态步骤。");
            }

            CheckUniqueIds(item.Rules, rule => rule.Id, $"{path}.rules", issues);
            foreach (var rule in item.Rules)
            {
                ValidateRule(rule, targets, bindings, models, $"{path}.rules[{rule.Id}]", issues);
            }

            return;
        }

        if (item.PoseSteps.Count == 0)
        {
            AddError(issues, "CFG-POSE-001", $"{path}.poseSteps", "姿态时序测试项至少需要一个动作步骤。");
        }

        if (item.Rules.Count > 0)
        {
            AddError(issues, "CFG-POSE-002", $"{path}.rules", "姿态时序测试项不能包含普通数量规则。");
        }

        CheckUniqueIds(item.PoseSteps, step => step.Id, $"{path}.poseSteps", issues);
        CheckPositiveUniqueOrders(item.PoseSteps.Select(step => step.Order), $"{path}.poseSteps", issues);
        foreach (var step in item.PoseSteps)
        {
            ValidatePoseStep(step, bindings, models, $"{path}.poseSteps[{step.Id}]", issues);
        }
    }

    private static void ValidateRule(
        TargetRuleDefinition rule,
        IReadOnlyDictionary<Guid, TargetDefinition> targets,
        IReadOnlyDictionary<Guid, (TargetDefinition target, ModelBindingDefinition binding)> bindings,
        IReadOnlyDictionary<Guid, ModelDefinition> models,
        string path,
        List<ConfigurationValidationIssue> issues)
    {
        RequireId(rule.Id, $"{path}.id", issues);
        if (!targets.TryGetValue(rule.TargetId, out var target))
        {
            AddError(issues, "CFG-RULE-001", $"{path}.targetId", "规则引用的目标不存在。");
            return;
        }

        if (!bindings.TryGetValue(rule.ModelBindingId, out var bindingPair))
        {
            AddError(issues, "CFG-RULE-002", $"{path}.modelBindingId", "规则引用的模型绑定不存在。");
            return;
        }

        if (bindingPair.target.Id != target.Id)
        {
            AddError(issues, "CFG-RULE-003", $"{path}.modelBindingId", "规则的模型绑定不属于所选目标。");
        }

        if (rule.ConfidenceThreshold is < 0 or > 1)
        {
            AddError(issues, "CFG-RULE-004", $"{path}.confidenceThreshold", "置信度阈值必须位于 0 到 1 之间。");
        }

        if (rule.OutcomeWhenMatched == InspectionVerdict.Error)
        {
            AddError(issues, "CFG-RULE-006", $"{path}.outcomeWhenMatched", "规则匹配结果只能配置为通过或不通过；错误仅用于运行异常。");
        }

        ValidateScope(rule.Scope, $"{path}.scope", issues);
        ValidateCountParameters(rule, path, issues);

        if (models.TryGetValue(bindingPair.binding.ModelId, out var model) &&
            model.TaskType == ModelTaskType.Classification &&
            rule.Metric != QuantityMetric.Presence)
        {
            AddError(issues, "CFG-RULE-005", $"{path}.metric", "分类模型不能使用实例数量指标。");
        }
    }

    private static void ValidateScope(
        RegionScopeDefinition scope,
        string path,
        List<ConfigurationValidationIssue> issues)
    {
        if (scope.Type == RegionType.FullImage)
        {
            if (scope.Regions.Count > 0)
            {
                AddError(issues, "CFG-ROI-001", $"{path}.regions", "全图范围不能包含 ROI 坐标。");
            }

            return;
        }

        if (scope.Regions.Count == 0)
        {
            AddError(issues, "CFG-ROI-002", $"{path}.regions", "ROI 范围至少需要一个矩形区域。");
        }

        CheckUniqueIds(scope.Regions, region => region.Id, $"{path}.regions", issues);
        foreach (var region in scope.Regions)
        {
            var regionPath = $"{path}.regions[{region.Id}]";
            RequireId(region.Id, $"{regionPath}.id", issues);
            RequireText(region.Name, $"{regionPath}.name", issues);
            if (region.ReferenceWidth <= 0 || region.ReferenceHeight <= 0)
            {
                AddError(issues, "CFG-ROI-003", regionPath, "ROI 参考图像宽高必须大于 0。");
                continue;
            }

            if (region.X1 < 0 || region.Y1 < 0 || region.X1 >= region.X2 || region.Y1 >= region.Y2)
            {
                AddError(issues, "CFG-ROI-004", regionPath, "ROI 坐标必须非负且满足 x1 < x2、y1 < y2。");
            }

            if (region.X2 > region.ReferenceWidth || region.Y2 > region.ReferenceHeight)
            {
                AddError(issues, "CFG-ROI-005", regionPath, "ROI 坐标超出参考图像边界。");
            }
        }
    }

    private static void ValidateCountParameters(
        TargetRuleDefinition rule,
        string path,
        List<ConfigurationValidationIssue> issues)
    {
        if (rule.Threshold < 0)
        {
            AddError(issues, "CFG-RULE-006", $"{path}.threshold", "数量阈值不能为负数。");
        }

        if (rule.Metric == QuantityMetric.MissingCount && rule.ExpectedCount is null)
        {
            AddError(issues, "CFG-RULE-007", $"{path}.expectedCount", "缺失数量指标必须配置预期数量。");
        }

        if (rule.ExpectedCount < 0)
        {
            AddError(issues, "CFG-RULE-008", $"{path}.expectedCount", "预期数量不能为负数。");
        }

        if (rule.Operator == ComparisonOperator.BetweenInclusive)
        {
            if (rule.UpperThreshold is null)
            {
                AddError(issues, "CFG-RULE-009", $"{path}.upperThreshold", "闭区间比较必须配置上限。");
            }
            else if (rule.UpperThreshold < rule.Threshold)
            {
                AddError(issues, "CFG-RULE-010", $"{path}.upperThreshold", "闭区间上限不能小于下限。");
            }
        }
    }

    private static void ValidatePoseStep(
        PoseStepDefinition step,
        IReadOnlyDictionary<Guid, (TargetDefinition target, ModelBindingDefinition binding)> bindings,
        IReadOnlyDictionary<Guid, ModelDefinition> models,
        string path,
        List<ConfigurationValidationIssue> issues)
    {
        RequireId(step.Id, $"{path}.id", issues);
        RequireText(step.Name, $"{path}.name", issues);
        RequireText(step.ActionCondition, $"{path}.actionCondition", issues);
        if (step.ConfidenceThreshold is < 0 or > 1)
        {
            AddError(issues, "CFG-POSE-003", $"{path}.confidenceThreshold", "置信度阈值必须位于 0 到 1 之间。");
        }

        if (step.MinimumHoldMs < 0 || step.MaximumWaitMs <= 0 || step.MinimumHoldMs > step.MaximumWaitMs)
        {
            AddError(issues, "CFG-POSE-004", path, "动作保持时间和最大等待时间配置无效。");
        }

        if (!bindings.TryGetValue(step.ModelBindingId, out var pair) || !models.TryGetValue(pair.binding.ModelId, out var model))
        {
            AddError(issues, "CFG-POSE-005", $"{path}.modelBindingId", "动作步骤引用的模型绑定不存在。");
        }
        else if (model.TaskType is not (ModelTaskType.Pose or ModelTaskType.Temporal))
        {
            AddError(issues, "CFG-POSE-006", $"{path}.modelBindingId", "动作步骤必须绑定姿态或时序模型。");
        }
    }

    private static void CheckPositiveUniqueOrders(
        IEnumerable<int> orders,
        string path,
        List<ConfigurationValidationIssue> issues)
    {
        var materialized = orders.ToArray();
        if (materialized.Any(order => order <= 0))
        {
            AddError(issues, "CFG-ORDER-001", path, "顺序号必须从正整数开始。");
        }

        if (materialized.Distinct().Count() != materialized.Length)
        {
            AddError(issues, "CFG-ORDER-002", path, "顺序号不能重复。");
        }
    }

    private static void CheckUniqueIds<T>(
        IEnumerable<T> values,
        Func<T, Guid> getId,
        string path,
        List<ConfigurationValidationIssue> issues)
    {
        foreach (var group in values.GroupBy(getId).Where(group => group.Count() > 1))
        {
            AddError(issues, "CFG-ID-001", path, $"ID {group.Key} 重复。");
        }
    }

    private static void RequireId(Guid value, string path, List<ConfigurationValidationIssue> issues)
    {
        if (value == Guid.Empty)
        {
            AddError(issues, "CFG-REQUIRED-001", path, "ID 不能为空。");
        }
    }

    private static void RequireText(string? value, string path, List<ConfigurationValidationIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            AddError(issues, "CFG-REQUIRED-002", path, "文本值不能为空。");
        }
    }

    private static void AddError(
        ICollection<ConfigurationValidationIssue> issues,
        string code,
        string path,
        string message) =>
        issues.Add(new ConfigurationValidationIssue(ConfigurationValidationSeverity.Error, code, path, message));
}
