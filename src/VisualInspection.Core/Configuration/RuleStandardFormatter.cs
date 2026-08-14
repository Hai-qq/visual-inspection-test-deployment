using VisualInspection.Core.Domain;
using VisualInspection.Core.Rules;

namespace VisualInspection.Core.Configuration;

public static class RuleStandardFormatter
{
    public static string Format(TargetRuleDefinition rule, ProjectConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(configuration);

        var targetName = configuration.Targets.FirstOrDefault(target => target.Id == rule.TargetId)?.Name ?? "未知目标";
        var scopeName = rule.Scope.Type == RegionType.FullImage
            ? "全图"
            : string.Join(" + ", rule.Scope.Regions.Select(region => region.Name));
        var metricName = rule.Metric switch
        {
            QuantityMetric.PresentCount => "在场数量",
            QuantityMetric.MissingCount => "缺失数量",
            QuantityMetric.Presence => "是否存在",
            _ => rule.Metric.ToString()
        };
        var comparison = rule.Operator switch
        {
            ComparisonOperator.Equal => $"= {rule.Threshold}",
            ComparisonOperator.NotEqual => $"!= {rule.Threshold}",
            ComparisonOperator.GreaterThan => $"> {rule.Threshold}",
            ComparisonOperator.GreaterThanOrEqual => $">= {rule.Threshold}",
            ComparisonOperator.LessThan => $"< {rule.Threshold}",
            ComparisonOperator.LessThanOrEqual => $"<= {rule.Threshold}",
            ComparisonOperator.BetweenInclusive => $"在闭区间 [{rule.Threshold}, {rule.UpperThreshold}] 内",
            _ => rule.Operator.ToString()
        };
        var outcome = rule.OutcomeWhenMatched == InspectionVerdict.Pass ? "通过" : "不通过";

        return $"{scopeName} · {targetName} {metricName} {comparison} → {outcome}";
    }
}
