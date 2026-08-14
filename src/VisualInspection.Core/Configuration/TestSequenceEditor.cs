using VisualInspection.Core.Domain;
using VisualInspection.Core.Rules;

namespace VisualInspection.Core.Configuration;

public static class TestSequenceEditor
{
    public static (TestSequenceDefinition Sequence, TestItemDefinition Item) AddNormalItem(
        ProjectConfiguration project,
        TestSequenceDefinition sequence,
        string itemName)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(sequence);

        if (string.IsNullOrWhiteSpace(itemName))
        {
            throw new ArgumentException("检测项名称不能为空。", nameof(itemName));
        }

        var candidate = FindNormalRuleBinding(project)
            ?? throw new InvalidOperationException("请先创建带有效模型绑定的普通视觉目标。");
        var nextOrder = sequence.Items.Select(item => item.Order).DefaultIfEmpty().Max() + 1;
        var item = new TestItemDefinition
        {
            Order = nextOrder,
            Name = itemName.Trim(),
            Type = TestItemType.Normal,
            Enabled = true,
            IsRequired = true,
            RuleOperator = RuleLogicalOperator.And,
            Rules =
            [
                new TargetRuleDefinition
                {
                    TargetId = candidate.Target.Id,
                    ModelBindingId = candidate.Binding.Id,
                    Scope = new RegionScopeDefinition { Type = RegionType.FullImage },
                    Metric = candidate.Model.TaskType == ModelTaskType.Classification
                        ? QuantityMetric.Presence
                        : QuantityMetric.PresentCount,
                    Operator = ComparisonOperator.Equal,
                    Threshold = 1,
                    ConfidenceThreshold = 0.5,
                    OutcomeWhenMatched = InspectionVerdict.Pass
                }
            ]
        };

        return (sequence with { Items = [.. sequence.Items, item] }, item);
    }

    private static (TargetDefinition Target, ModelBindingDefinition Binding, ModelDefinition Model)? FindNormalRuleBinding(
        ProjectConfiguration project)
    {
        foreach (var target in project.Targets)
        {
            foreach (var binding in target.ModelBindings)
            {
                var model = project.Models.FirstOrDefault(candidate => candidate.Id == binding.ModelId);
                if (model is null ||
                    model.TaskType is ModelTaskType.Pose or ModelTaskType.Temporal ||
                    !string.Equals(binding.ModelVersion, model.Version, StringComparison.Ordinal) ||
                    model.Labels.All(label => label.Id != binding.OutputLabelId))
                {
                    continue;
                }

                return (target, binding, model);
            }
        }

        return null;
    }

    public static TestSequenceDefinition RemoveItem(TestSequenceDefinition sequence, Guid itemId)
    {
        ArgumentNullException.ThrowIfNull(sequence);

        if (sequence.Items.All(item => item.Id != itemId))
        {
            throw new InvalidOperationException("所选测试项已不存在。");
        }

        if (sequence.Items.Count <= 1)
        {
            throw new InvalidOperationException("测试序列至少需要一个测试项。");
        }

        var remainingItems = sequence.Items
            .Where(item => item.Id != itemId)
            .OrderBy(item => item.Order)
            .Select((item, index) => item with { Order = index + 1 })
            .ToList();
        return sequence with { Items = remainingItems };
    }
}
