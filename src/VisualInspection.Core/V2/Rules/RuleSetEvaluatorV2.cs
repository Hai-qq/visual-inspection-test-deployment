using VisualInspection.Core.V2.Configuration;
using VisualInspection.Core.V2.Execution;

namespace VisualInspection.Core.V2.Rules;

public sealed record RuleEvaluationV2
{
    public required Guid RuleId { get; init; }
    public required int MetricValue { get; init; }
    public required bool ConditionMatched { get; init; }
    public required BusinessVerdict Verdict { get; init; }
}

public sealed record RuleSetEvaluationV2
{
    public required BusinessVerdict Verdict { get; init; }
    public required IReadOnlyList<RuleEvaluationV2> Rules { get; init; }
}

public static class RuleSetEvaluatorV2
{
    public static RuleSetEvaluationV2 Evaluate(
        RuleSetDefinition ruleSet,
        IReadOnlyDictionary<Guid, ModelObservation> observations)
    {
        ArgumentNullException.ThrowIfNull(ruleSet);
        ArgumentNullException.ThrowIfNull(observations);
        if (ruleSet.Rules.Count == 0)
        {
            throw new InvalidDataException("RuleSet 至少需要一条规则。");
        }

        var evaluations = ruleSet.Rules.Select(rule => EvaluateRule(rule, observations)).ToArray();
        var pass = ruleSet.LogicalOperator == RuleLogicalOperatorV2.And
            ? evaluations.All(evaluation => evaluation.Verdict == BusinessVerdict.Pass)
            : evaluations.Any(evaluation => evaluation.Verdict == BusinessVerdict.Pass);
        return new RuleSetEvaluationV2
        {
            Verdict = pass ? BusinessVerdict.Pass : BusinessVerdict.Fail,
            Rules = evaluations
        };
    }

    private static RuleEvaluationV2 EvaluateRule(
        InspectionRuleDefinition rule,
        IReadOnlyDictionary<Guid, ModelObservation> observations)
    {
        if (!observations.TryGetValue(rule.ModelBindingId, out var observation))
        {
            throw new InvalidDataException($"规则 {rule.RuleId} 没有对应的模型 Observation。");
        }

        var presentCount = observation.Detections.Count > 0
            ? CountDetections(rule, observation)
            : observation.GetCount(rule.ModelBindingId, rule.OutputLabelId);
        var metricValue = rule.Metric switch
        {
            RuleMetricV2.MissingCount => Math.Max(0, rule.ExpectedCount.GetValueOrDefault() - presentCount),
            RuleMetricV2.Presence => presentCount > 0 ? 1 : 0,
            _ => presentCount
        };
        var matched = rule.Operator switch
        {
            RuleComparisonOperatorV2.Equal => metricValue == rule.Threshold,
            RuleComparisonOperatorV2.NotEqual => metricValue != rule.Threshold,
            RuleComparisonOperatorV2.GreaterThan => metricValue > rule.Threshold,
            RuleComparisonOperatorV2.GreaterThanOrEqual => metricValue >= rule.Threshold,
            RuleComparisonOperatorV2.LessThan => metricValue < rule.Threshold,
            RuleComparisonOperatorV2.LessThanOrEqual => metricValue <= rule.Threshold,
            RuleComparisonOperatorV2.BetweenInclusive =>
                rule.UpperThreshold is int upper && metricValue >= rule.Threshold && metricValue <= upper,
            _ => false
        };
        var matchedVerdict = rule.OutcomeWhenMatched == RuleOutcome.Pass
            ? BusinessVerdict.Pass
            : BusinessVerdict.Fail;
        return new RuleEvaluationV2
        {
            RuleId = rule.RuleId,
            MetricValue = metricValue,
            ConditionMatched = matched,
            Verdict = matched
                ? matchedVerdict
                : matchedVerdict == BusinessVerdict.Pass ? BusinessVerdict.Fail : BusinessVerdict.Pass
        };
    }

    private static int CountDetections(InspectionRuleDefinition rule, ModelObservation observation)
    {
        if (observation.FrameWidth <= 0 || observation.FrameHeight <= 0)
        {
            throw new InvalidDataException("带空间检测框的 Observation 缺少有效 Frame 尺寸。");
        }

        return observation.Detections.Count(detection =>
        {
            if (detection.ModelBindingId != rule.ModelBindingId ||
                detection.OutputLabelId != rule.OutputLabelId ||
                detection.Confidence < rule.ConfidenceThreshold)
            {
                return false;
            }

            if (!double.IsFinite(detection.X1) || !double.IsFinite(detection.Y1) ||
                !double.IsFinite(detection.X2) || !double.IsFinite(detection.Y2) ||
                detection.X1 < 0 || detection.Y1 < 0 || detection.X2 <= detection.X1 ||
                detection.Y2 <= detection.Y1 || detection.X2 > observation.FrameWidth ||
                detection.Y2 > observation.FrameHeight || detection.Confidence is < 0 or > 1)
            {
                throw new InvalidDataException("检测框坐标或 Confidence 无效。");
            }

            if (rule.Scope.Type == RegionScopeTypeV2.FullImage)
            {
                return true;
            }

            return rule.Scope.Regions.Any(region =>
            {
                var centerX = detection.CenterX * region.ReferenceWidth / observation.FrameWidth;
                var centerY = detection.CenterY * region.ReferenceHeight / observation.FrameHeight;
                return centerX >= region.X1 && centerX <= region.X2 &&
                       centerY >= region.Y1 && centerY <= region.Y2;
            });
        });
    }
}
