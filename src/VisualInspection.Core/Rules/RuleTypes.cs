namespace VisualInspection.Core.Rules;

public enum QuantityMetric
{
    PresentCount,
    MissingCount,
    Presence
}

public enum ComparisonOperator
{
    Equal,
    NotEqual,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual,
    BetweenInclusive
}

public enum RuleLogicalOperator
{
    And,
    Or
}
