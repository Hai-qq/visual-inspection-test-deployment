using VisualInspection.App.ViewModels;
using VisualInspection.Core.Configuration;
using VisualInspection.Core.Domain;
using VisualInspection.Core.Rules;

namespace VisualInspection.App.Tests;

public sealed class DetectionSummaryRowViewModelTests
{
    [Theory]
    [InlineData(ComparisonOperator.Equal, 2, -1, "数量 = 2 → 通过")]
    [InlineData(ComparisonOperator.NotEqual, 2, -1, "数量 ≠ 2 → 通过")]
    [InlineData(ComparisonOperator.GreaterThan, 2, -1, "数量 > 2 → 通过")]
    [InlineData(ComparisonOperator.GreaterThanOrEqual, 2, -1, "数量 ≥ 2 → 通过")]
    [InlineData(ComparisonOperator.LessThan, 2, -1, "数量 < 2 → 通过")]
    [InlineData(ComparisonOperator.LessThanOrEqual, 2, -1, "数量 ≤ 2 → 通过")]
    [InlineData(ComparisonOperator.BetweenInclusive, 2, 4, "数量 2–4 → 通过")]
    public void PendingRow_FormatsConfiguredComparison(
        ComparisonOperator comparisonOperator,
        int threshold,
        int upperThreshold,
        string expectedLogic)
    {
        var rule = Rule(
            QuantityMetric.PresentCount,
            comparisonOperator,
            threshold,
            upperThreshold < 0 ? null : upperThreshold);

        var row = new DetectionSummaryRowViewModel("标签", rule);

        Assert.Equal(expectedLogic, row.LogicText);
        Assert.Equal("待检测", row.MeasuredText);
        Assert.Equal("—", row.Result);
    }

    [Theory]
    [InlineData(1, 7, "存在 → 通过", "存在 · 7 个", "PASS")]
    [InlineData(0, 0, "不存在 → 通过", "不存在 · 0 个", "PASS")]
    public void EvaluatedPresenceRow_ShowsExistenceAndRecognizedCount(
        int threshold,
        int detectedCount,
        string expectedLogic,
        string expectedMeasured,
        string expectedResult)
    {
        var rule = Rule(QuantityMetric.Presence, ComparisonOperator.Equal, threshold);
        var evaluation = Evaluate(rule, detectedCount);

        var row = new DetectionSummaryRowViewModel("标签", rule, detectedCount, evaluation);

        Assert.Equal(expectedLogic, row.LogicText);
        Assert.Equal(expectedMeasured, row.MeasuredText);
        Assert.Equal(expectedResult, row.Result);
    }

    [Fact]
    public void EvaluatedMissingCountRow_ShowsExpectedDetectedAndMissingValues()
    {
        var rule = Rule(
            QuantityMetric.MissingCount,
            ComparisonOperator.Equal,
            threshold: 0,
            expectedCount: 4);
        var evaluation = Evaluate(rule, detectedCount: 3);

        var row = new DetectionSummaryRowViewModel("螺钉", rule, 3, evaluation);

        Assert.Equal("缺失数量 = 0（应有 4） → 通过", row.LogicText);
        Assert.Equal("缺 1（识别 3）", row.MeasuredText);
        Assert.Equal("预期 4 个，本次识别 3 个，缺失 1 个。", row.MeasuredDetailText);
        Assert.Equal("FAIL", row.Result);
    }

    [Fact]
    public void DefectRule_ShowsMatchedOutcomeDirection()
    {
        var rule = Rule(
            QuantityMetric.PresentCount,
            ComparisonOperator.GreaterThan,
            threshold: 0,
            outcomeWhenMatched: InspectionVerdict.Fail);
        var evaluation = Evaluate(rule, detectedCount: 0);

        var row = new DetectionSummaryRowViewModel("反向标签", rule, 0, evaluation);

        Assert.Equal("数量 > 0 → 不通过", row.LogicText);
        Assert.Equal("0 个", row.MeasuredText);
        Assert.Equal("PASS", row.Result);
    }

    private static TargetRuleDefinition Rule(
        QuantityMetric metric,
        ComparisonOperator comparisonOperator,
        int threshold,
        int? upperThreshold = null,
        int? expectedCount = null,
        InspectionVerdict outcomeWhenMatched = InspectionVerdict.Pass) => new()
        {
            Metric = metric,
            Operator = comparisonOperator,
            Threshold = threshold,
            UpperThreshold = upperThreshold,
            ExpectedCount = expectedCount,
            OutcomeWhenMatched = outcomeWhenMatched
        };

    private static RuleEvaluationResult Evaluate(TargetRuleDefinition rule, int detectedCount) =>
        CountRuleEvaluator.Evaluate(
            new CountRule(
                "标签",
                rule.Metric,
                rule.Operator,
                rule.Threshold,
                rule.UpperThreshold,
                rule.ExpectedCount,
                rule.OutcomeWhenMatched),
            detectedCount);
}
