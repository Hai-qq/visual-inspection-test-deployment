using VisualInspection.Core.Domain;
using VisualInspection.Core.Rules;

namespace VisualInspection.Core.Tests;

public sealed class RuleGroupEvaluatorTests
{
    private static readonly RuleEvaluationResult Pass = new("Target", 1, true, InspectionVerdict.Pass);
    private static readonly RuleEvaluationResult Fail = new("Target", 0, false, InspectionVerdict.Fail);

    [Fact]
    public void Evaluate_AndRequiresEveryRuleToPass()
    {
        var verdict = RuleGroupEvaluator.Evaluate([Pass, Fail], RuleLogicalOperator.And);

        Assert.Equal(InspectionVerdict.Fail, verdict);
    }

    [Fact]
    public void Evaluate_OrRequiresAtLeastOneRuleToPass()
    {
        var verdict = RuleGroupEvaluator.Evaluate([Pass, Fail], RuleLogicalOperator.Or);

        Assert.Equal(InspectionVerdict.Pass, verdict);
    }

    [Fact]
    public void Evaluate_RejectsEmptyRuleGroup()
    {
        Assert.Throws<ArgumentException>(
            () => RuleGroupEvaluator.Evaluate([], RuleLogicalOperator.And));
    }
}
