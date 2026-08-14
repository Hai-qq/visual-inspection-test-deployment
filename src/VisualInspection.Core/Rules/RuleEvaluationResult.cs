using VisualInspection.Core.Domain;

namespace VisualInspection.Core.Rules;

public sealed record RuleEvaluationResult(
    string TargetName,
    int MetricValue,
    bool ConditionMatched,
    InspectionVerdict Verdict);
