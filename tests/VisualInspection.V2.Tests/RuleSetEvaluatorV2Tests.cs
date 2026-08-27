using VisualInspection.Core.V2.Configuration;
using VisualInspection.Core.V2.Execution;
using VisualInspection.Core.V2.Rules;

namespace VisualInspection.V2.Tests;

public sealed class RuleSetEvaluatorV2Tests
{
    [Theory]
    [InlineData(0, BusinessVerdict.Pass)]
    [InlineData(1, BusinessVerdict.Fail)]
    [InlineData(3, BusinessVerdict.Fail)]
    public void DefectCountGreaterThanZero_CanExplicitlyMeanFail(int count, BusinessVerdict expected)
    {
        var bindingId = Guid.NewGuid();
        var rule = Rule(bindingId, RuleComparisonOperatorV2.GreaterThan, 0, RuleOutcome.Fail);

        var result = RuleSetEvaluatorV2.Evaluate(
            new RuleSetDefinition { Rules = [rule] },
            Observations(bindingId, count));

        Assert.Equal(expected, result.Verdict);
    }

    [Theory]
    [InlineData(RuleComparisonOperatorV2.Equal, 2, 2, true)]
    [InlineData(RuleComparisonOperatorV2.NotEqual, 2, 3, true)]
    [InlineData(RuleComparisonOperatorV2.GreaterThan, 2, 3, true)]
    [InlineData(RuleComparisonOperatorV2.GreaterThanOrEqual, 2, 2, true)]
    [InlineData(RuleComparisonOperatorV2.LessThan, 2, 1, true)]
    [InlineData(RuleComparisonOperatorV2.LessThanOrEqual, 2, 2, true)]
    public void ComparisonOperators_AreSupported(
        RuleComparisonOperatorV2 comparison,
        int threshold,
        int actual,
        bool expectedMatch)
    {
        var bindingId = Guid.NewGuid();
        var rule = Rule(bindingId, comparison, threshold, RuleOutcome.Pass);

        var result = RuleSetEvaluatorV2.Evaluate(
            new RuleSetDefinition { Rules = [rule] },
            Observations(bindingId, actual));

        Assert.Equal(expectedMatch, result.Rules[0].ConditionMatched);
    }

    [Fact]
    public void BetweenInclusive_IsSupported()
    {
        var bindingId = Guid.NewGuid();
        var rule = Rule(bindingId, RuleComparisonOperatorV2.BetweenInclusive, 2, RuleOutcome.Pass) with
        {
            UpperThreshold = 4
        };

        var result = RuleSetEvaluatorV2.Evaluate(
            new RuleSetDefinition { Rules = [rule] },
            Observations(bindingId, 4));

        Assert.True(result.Rules[0].ConditionMatched);
        Assert.Equal(BusinessVerdict.Pass, result.Verdict);
    }

    [Fact]
    public void AndAndOr_CombineMultipleRuleVerdicts()
    {
        var bindingId = Guid.NewGuid();
        var pass = Rule(bindingId, RuleComparisonOperatorV2.Equal, 1, RuleOutcome.Pass);
        var fail = Rule(bindingId, RuleComparisonOperatorV2.Equal, 2, RuleOutcome.Pass);
        var observations = Observations(bindingId, 1);

        var andResult = RuleSetEvaluatorV2.Evaluate(
            new RuleSetDefinition { LogicalOperator = RuleLogicalOperatorV2.And, Rules = [pass, fail] },
            observations);
        var orResult = RuleSetEvaluatorV2.Evaluate(
            new RuleSetDefinition { LogicalOperator = RuleLogicalOperatorV2.Or, Rules = [pass, fail] },
            observations);

        Assert.Equal(BusinessVerdict.Fail, andResult.Verdict);
        Assert.Equal(BusinessVerdict.Pass, orResult.Verdict);
    }

    [Fact]
    public void ConfidenceAndRoi_FilterDetections()
    {
        var bindingId = Guid.NewGuid();
        var rule = Rule(bindingId, RuleComparisonOperatorV2.Equal, 1, RuleOutcome.Pass) with
        {
            ConfidenceThreshold = 0.8,
            Scope = new RegionScopeDefinitionV2
            {
                Type = RegionScopeTypeV2.Roi,
                Regions =
                [
                    new RegionOfInterestV2
                    {
                        Name = "center",
                        X1 = 20,
                        Y1 = 20,
                        X2 = 80,
                        Y2 = 80,
                        ReferenceWidth = 100,
                        ReferenceHeight = 100
                    }
                ]
            }
        };
        var observation = new ModelObservation
        {
            CaptureId = Guid.NewGuid(),
            ModelBindingId = bindingId,
            ObservedAtUtc = DateTimeOffset.UtcNow,
            FrameWidth = 100,
            FrameHeight = 100,
            Detections =
            [
                Detection(bindingId, 40, 40, 60, 60, 0.9),
                Detection(bindingId, 40, 40, 60, 60, 0.7),
                Detection(bindingId, 0, 0, 10, 10, 0.95)
            ]
        };

        var result = RuleSetEvaluatorV2.Evaluate(
            new RuleSetDefinition { Rules = [rule] },
            new Dictionary<Guid, ModelObservation> { [bindingId] = observation });

        Assert.Equal(1, result.Rules[0].MetricValue);
        Assert.Equal(BusinessVerdict.Pass, result.Verdict);
    }

    [Fact]
    public void MissingCountAndPresence_AreSupported()
    {
        var bindingId = Guid.NewGuid();
        var missing = Rule(bindingId, RuleComparisonOperatorV2.Equal, 2, RuleOutcome.Pass) with
        {
            Metric = RuleMetricV2.MissingCount,
            ExpectedCount = 5
        };
        var presence = Rule(bindingId, RuleComparisonOperatorV2.Equal, 1, RuleOutcome.Pass) with
        {
            Metric = RuleMetricV2.Presence
        };
        var observations = Observations(bindingId, 3);

        var missingResult = RuleSetEvaluatorV2.Evaluate(new RuleSetDefinition { Rules = [missing] }, observations);
        var presenceResult = RuleSetEvaluatorV2.Evaluate(new RuleSetDefinition { Rules = [presence] }, observations);

        Assert.Equal(BusinessVerdict.Pass, missingResult.Verdict);
        Assert.Equal(BusinessVerdict.Pass, presenceResult.Verdict);
    }

    private static InspectionRuleDefinition Rule(
        Guid bindingId,
        RuleComparisonOperatorV2 comparison,
        int threshold,
        RuleOutcome outcome) => new()
        {
            ModelBindingId = bindingId,
            OutputLabelId = 0,
            Metric = RuleMetricV2.PresentCount,
            Operator = comparison,
            Threshold = threshold,
            OutcomeWhenMatched = outcome,
            Scope = new RegionScopeDefinitionV2()
        };

    private static IReadOnlyDictionary<Guid, ModelObservation> Observations(Guid bindingId, int count) =>
        new Dictionary<Guid, ModelObservation>
        {
            [bindingId] = new ModelObservation
            {
                CaptureId = Guid.NewGuid(),
                ModelBindingId = bindingId,
                ObservedAtUtc = DateTimeOffset.UtcNow,
                Counts = new Dictionary<ModelOutputKey, int>
                {
                    [new ModelOutputKey(bindingId, 0)] = count
                }
            }
        };

    private static ModelDetectionV2 Detection(
        Guid bindingId,
        double x1,
        double y1,
        double x2,
        double y2,
        double confidence) => new()
        {
            ModelBindingId = bindingId,
            OutputLabelId = 0,
            X1 = x1,
            Y1 = y1,
            X2 = x2,
            Y2 = y2,
            Confidence = confidence
        };
}
