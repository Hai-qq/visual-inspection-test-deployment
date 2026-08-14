using VisualInspection.Core.Domain;

namespace VisualInspection.Core.Rules;

public sealed record CountRule(
    string TargetName,
    QuantityMetric Metric,
    ComparisonOperator Operator,
    int Threshold,
    int? UpperThreshold = null,
    int? ExpectedCount = null,
    InspectionVerdict OutcomeWhenMatched = InspectionVerdict.Pass);
