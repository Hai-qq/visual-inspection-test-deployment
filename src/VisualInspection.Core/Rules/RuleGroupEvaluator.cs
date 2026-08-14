using VisualInspection.Core.Domain;

namespace VisualInspection.Core.Rules;

public static class RuleGroupEvaluator
{
    public static InspectionVerdict Evaluate(
        IEnumerable<RuleEvaluationResult> results,
        RuleLogicalOperator logicalOperator)
    {
        ArgumentNullException.ThrowIfNull(results);

        var materialized = results.ToArray();
        if (materialized.Length == 0)
        {
            throw new ArgumentException("至少需要一条规则结果。", nameof(results));
        }

        var passes = logicalOperator switch
        {
            RuleLogicalOperator.And => materialized.All(result => result.Verdict == InspectionVerdict.Pass),
            RuleLogicalOperator.Or => materialized.Any(result => result.Verdict == InspectionVerdict.Pass),
            _ => throw new ArgumentOutOfRangeException(
                nameof(logicalOperator),
                logicalOperator,
                "不支持此逻辑运算符。")
        };

        return passes ? InspectionVerdict.Pass : InspectionVerdict.Fail;
    }
}
