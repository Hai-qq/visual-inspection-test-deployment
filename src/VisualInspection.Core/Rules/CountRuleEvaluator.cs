using VisualInspection.Core.Domain;

namespace VisualInspection.Core.Rules;

public static class CountRuleEvaluator
{
    public static RuleEvaluationResult Evaluate(CountRule rule, int presentCount)
    {
        ArgumentNullException.ThrowIfNull(rule);
        Validate(rule, presentCount);

        var metricValue = rule.Metric switch
        {
            QuantityMetric.PresentCount => presentCount,
            QuantityMetric.MissingCount => Math.Max(rule.ExpectedCount!.Value - presentCount, 0),
            QuantityMetric.Presence => presentCount > 0 ? 1 : 0,
            _ => throw new ArgumentOutOfRangeException(nameof(rule), rule.Metric, "不支持此数量指标。")
        };

        var matched = rule.Operator switch
        {
            ComparisonOperator.Equal => metricValue == rule.Threshold,
            ComparisonOperator.NotEqual => metricValue != rule.Threshold,
            ComparisonOperator.GreaterThan => metricValue > rule.Threshold,
            ComparisonOperator.GreaterThanOrEqual => metricValue >= rule.Threshold,
            ComparisonOperator.LessThan => metricValue < rule.Threshold,
            ComparisonOperator.LessThanOrEqual => metricValue <= rule.Threshold,
            ComparisonOperator.BetweenInclusive =>
                metricValue >= rule.Threshold && metricValue <= rule.UpperThreshold!.Value,
            _ => throw new ArgumentOutOfRangeException(nameof(rule), rule.Operator, "不支持此比较运算符。")
        };

        var verdict = matched
            ? rule.OutcomeWhenMatched
            : Opposite(rule.OutcomeWhenMatched);

        return new RuleEvaluationResult(rule.TargetName, metricValue, matched, verdict);
    }

    private static void Validate(CountRule rule, int presentCount)
    {
        if (string.IsNullOrWhiteSpace(rule.TargetName))
        {
            throw new ArgumentException("必须提供目标名称。", nameof(rule));
        }

        if (presentCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(presentCount), "在场数量不能为负数。");
        }

        if (rule.Threshold < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rule), "数量阈值不能为负数。");
        }

        if (rule.Metric == QuantityMetric.MissingCount && rule.ExpectedCount is null)
        {
            throw new ArgumentException("使用缺失数量指标时必须配置预期数量。", nameof(rule));
        }

        if (rule.ExpectedCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rule), "预期数量不能为负数。");
        }

        if (rule.Operator == ComparisonOperator.BetweenInclusive)
        {
            if (rule.UpperThreshold is null)
            {
                throw new ArgumentException("闭区间比较必须配置上限。", nameof(rule));
            }

            if (rule.UpperThreshold < rule.Threshold)
            {
                throw new ArgumentException("上限不能小于下限。", nameof(rule));
            }
        }
    }

    private static InspectionVerdict Opposite(InspectionVerdict verdict) => verdict switch
    {
        InspectionVerdict.Pass => InspectionVerdict.Fail,
        InspectionVerdict.Fail => InspectionVerdict.Pass,
        _ => throw new ArgumentOutOfRangeException(nameof(verdict), verdict, "不支持此判定结果。")
    };
}
