using System.Windows.Media;
using VisualInspection.Core.Configuration;
using VisualInspection.Core.Domain;
using VisualInspection.Core.Rules;

namespace VisualInspection.App.ViewModels;

public sealed class DetectionSummaryRowViewModel
{
    public DetectionSummaryRowViewModel(string label, TargetRuleDefinition rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        Label = label;
        LogicText = FormatLogic(rule);
        MeasuredText = "待检测";
        MeasuredDetailText = "尚未执行；该行判定逻辑已从当前测试序列加载。";
        Result = "—";
        ResultBrush = Brushes.SlateGray;
    }

    public DetectionSummaryRowViewModel(
        string label,
        TargetRuleDefinition rule,
        int detectedCount,
        RuleEvaluationResult evaluation)
        : this(label, rule)
    {
        ArgumentNullException.ThrowIfNull(evaluation);

        MeasuredText = FormatMeasured(rule, detectedCount, evaluation.MetricValue);
        MeasuredDetailText = FormatMeasuredDetail(rule, detectedCount, evaluation.MetricValue);
        Result = evaluation.Verdict switch
        {
            InspectionVerdict.Pass => "PASS",
            InspectionVerdict.Fail => "FAIL",
            InspectionVerdict.Error => "ERROR",
            _ => evaluation.Verdict.ToString().ToUpperInvariant()
        };
        ResultBrush = evaluation.Verdict switch
        {
            InspectionVerdict.Pass => new SolidColorBrush(Color.FromRgb(0, 112, 74)),
            InspectionVerdict.Fail => new SolidColorBrush(Color.FromRgb(201, 64, 58)),
            _ => Brushes.DarkOrange
        };
    }

    public string Label { get; }
    public string LogicText { get; }
    public string MeasuredText { get; private set; }
    public string MeasuredDetailText { get; private set; }
    public string Result { get; }
    public Brush ResultBrush { get; }

    private static string FormatLogic(TargetRuleDefinition rule)
    {
        var condition = rule.Metric switch
        {
            QuantityMetric.PresentCount => $"数量 {FormatComparison(rule)}",
            QuantityMetric.MissingCount => $"缺失数量 {FormatComparison(rule)}{FormatExpectedCount(rule)}",
            QuantityMetric.Presence => FormatPresenceCondition(rule),
            _ => $"{rule.Metric} {FormatComparison(rule)}"
        };
        var matchedOutcome = rule.OutcomeWhenMatched == InspectionVerdict.Pass ? "通过" : "不通过";
        return $"{condition} → {matchedOutcome}";
    }

    private static string FormatComparison(TargetRuleDefinition rule) => rule.Operator switch
    {
        ComparisonOperator.Equal => $"= {rule.Threshold}",
        ComparisonOperator.NotEqual => $"≠ {rule.Threshold}",
        ComparisonOperator.GreaterThan => $"> {rule.Threshold}",
        ComparisonOperator.GreaterThanOrEqual => $"≥ {rule.Threshold}",
        ComparisonOperator.LessThan => $"< {rule.Threshold}",
        ComparisonOperator.LessThanOrEqual => $"≤ {rule.Threshold}",
        ComparisonOperator.BetweenInclusive => $"{rule.Threshold}–{rule.UpperThreshold?.ToString() ?? "?"}",
        _ => rule.Operator.ToString()
    };

    private static string FormatExpectedCount(TargetRuleDefinition rule) => rule.ExpectedCount is { } expected
        ? $"（应有 {expected}）"
        : string.Empty;

    private static string FormatPresenceCondition(TargetRuleDefinition rule)
    {
        var absenceMatches = Matches(rule, 0);
        var presenceMatches = Matches(rule, 1);
        return (absenceMatches, presenceMatches) switch
        {
            (false, true) => "存在",
            (true, false) => "不存在",
            (true, true) => "存在状态不限",
            _ => $"存在状态 {FormatComparison(rule)}"
        };
    }

    private static bool Matches(TargetRuleDefinition rule, int value) => rule.Operator switch
    {
        ComparisonOperator.Equal => value == rule.Threshold,
        ComparisonOperator.NotEqual => value != rule.Threshold,
        ComparisonOperator.GreaterThan => value > rule.Threshold,
        ComparisonOperator.GreaterThanOrEqual => value >= rule.Threshold,
        ComparisonOperator.LessThan => value < rule.Threshold,
        ComparisonOperator.LessThanOrEqual => value <= rule.Threshold,
        ComparisonOperator.BetweenInclusive =>
            rule.UpperThreshold is { } upper && value >= rule.Threshold && value <= upper,
        _ => false
    };

    private static string FormatMeasured(
        TargetRuleDefinition rule,
        int detectedCount,
        int metricValue) => rule.Metric switch
        {
            QuantityMetric.PresentCount => $"{detectedCount} 个",
            QuantityMetric.MissingCount => $"缺 {metricValue}（识别 {detectedCount}）",
            QuantityMetric.Presence => detectedCount > 0 ? $"存在 · {detectedCount} 个" : "不存在 · 0 个",
            _ => metricValue.ToString()
        };

    private static string FormatMeasuredDetail(
        TargetRuleDefinition rule,
        int detectedCount,
        int metricValue) => rule.Metric switch
        {
            QuantityMetric.PresentCount => $"本次识别 {detectedCount} 个。",
            QuantityMetric.MissingCount =>
                $"预期 {rule.ExpectedCount?.ToString() ?? "未设置"} 个，本次识别 {detectedCount} 个，缺失 {metricValue} 个。",
            QuantityMetric.Presence => detectedCount > 0
                ? $"本次识别 {detectedCount} 个，状态为存在。"
                : "本次识别 0 个，状态为不存在。",
            _ => $"本次规则指标值为 {metricValue}。"
        };
}
