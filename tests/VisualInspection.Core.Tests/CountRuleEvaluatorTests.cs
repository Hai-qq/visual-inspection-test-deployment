using VisualInspection.Core.Domain;
using VisualInspection.Core.Rules;

namespace VisualInspection.Core.Tests;

public sealed class CountRuleEvaluatorTests
{
    [Theory]
    [InlineData(ComparisonOperator.Equal, 4, 4, true)]
    [InlineData(ComparisonOperator.NotEqual, 4, 3, true)]
    [InlineData(ComparisonOperator.GreaterThan, 0, 1, true)]
    [InlineData(ComparisonOperator.GreaterThanOrEqual, 4, 4, true)]
    [InlineData(ComparisonOperator.LessThan, 4, 3, true)]
    [InlineData(ComparisonOperator.LessThanOrEqual, 4, 4, true)]
    public void Evaluate_AppliesComparisonOperator(
        ComparisonOperator comparisonOperator,
        int threshold,
        int presentCount,
        bool expectedMatch)
    {
        var rule = new CountRule(
            "Screw",
            QuantityMetric.PresentCount,
            comparisonOperator,
            threshold);

        var result = CountRuleEvaluator.Evaluate(rule, presentCount);

        Assert.Equal(expectedMatch, result.ConditionMatched);
        Assert.Equal(InspectionVerdict.Pass, result.Verdict);
    }

    [Fact]
    public void Evaluate_BetweenInclusive_IncludesBothBoundaries()
    {
        var rule = new CountRule(
            "Label",
            QuantityMetric.PresentCount,
            ComparisonOperator.BetweenInclusive,
            2,
            UpperThreshold: 4);

        Assert.Equal(InspectionVerdict.Pass, CountRuleEvaluator.Evaluate(rule, 2).Verdict);
        Assert.Equal(InspectionVerdict.Pass, CountRuleEvaluator.Evaluate(rule, 4).Verdict);
        Assert.Equal(InspectionVerdict.Fail, CountRuleEvaluator.Evaluate(rule, 5).Verdict);
    }

    [Fact]
    public void Evaluate_MissingCount_UsesExpectedMinusPresentWithZeroFloor()
    {
        var rule = new CountRule(
            "Screw",
            QuantityMetric.MissingCount,
            ComparisonOperator.Equal,
            0,
            ExpectedCount: 4);

        var complete = CountRuleEvaluator.Evaluate(rule, 4);
        var overDetected = CountRuleEvaluator.Evaluate(rule, 6);
        var missing = CountRuleEvaluator.Evaluate(rule, 3);

        Assert.Equal(0, complete.MetricValue);
        Assert.Equal(0, overDetected.MetricValue);
        Assert.Equal(1, missing.MetricValue);
        Assert.Equal(InspectionVerdict.Fail, missing.Verdict);
    }

    [Fact]
    public void Evaluate_Presence_MapsCountToZeroOrOne()
    {
        var rule = new CountRule(
            "Part",
            QuantityMetric.Presence,
            ComparisonOperator.Equal,
            1);

        Assert.Equal(0, CountRuleEvaluator.Evaluate(rule, 0).MetricValue);
        Assert.Equal(1, CountRuleEvaluator.Evaluate(rule, 7).MetricValue);
    }

    [Fact]
    public void Evaluate_CanMapMatchedDefectConditionToFail()
    {
        var rule = new CountRule(
            "Defect",
            QuantityMetric.PresentCount,
            ComparisonOperator.GreaterThan,
            0,
            OutcomeWhenMatched: InspectionVerdict.Fail);

        Assert.Equal(InspectionVerdict.Fail, CountRuleEvaluator.Evaluate(rule, 1).Verdict);
        Assert.Equal(InspectionVerdict.Pass, CountRuleEvaluator.Evaluate(rule, 0).Verdict);
    }

    [Fact]
    public void Evaluate_RejectsMissingCountWithoutExpectedCount()
    {
        var rule = new CountRule(
            "Part",
            QuantityMetric.MissingCount,
            ComparisonOperator.Equal,
            0);

        Assert.Throws<ArgumentException>(() => CountRuleEvaluator.Evaluate(rule, 1));
    }

    [Fact]
    public void Evaluate_RejectsInvalidBetweenRange()
    {
        var rule = new CountRule(
            "Part",
            QuantityMetric.PresentCount,
            ComparisonOperator.BetweenInclusive,
            4,
            UpperThreshold: 2);

        Assert.Throws<ArgumentException>(() => CountRuleEvaluator.Evaluate(rule, 3));
    }
}
