using VisualInspection.Core.Configuration;

namespace VisualInspection.Core.Tests;

public sealed class RuleStandardFormatterTests
{
    [Fact]
    public void Format_ProducesReadableStandard()
    {
        var project = ConfigurationTestFactory.Create();
        var rule = project.TestSequences[0].Items[0].Rules[0];

        var standard = RuleStandardFormatter.Format(rule, project);

        Assert.Equal("全图 · Part 在场数量 = 1 → 通过", standard);
    }
}
