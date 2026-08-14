using VisualInspection.Core.Configuration;
using VisualInspection.Core.Domain;
using VisualInspection.Core.Rules;

namespace VisualInspection.Core.Tests;

public sealed class TestSequenceEditorTests
{
    [Fact]
    public void AddNormalItem_AppendsValidItemWithDefaultRule()
    {
        var project = ConfigurationTestFactory.Create();
        var sequence = project.TestSequences[0];

        var edit = TestSequenceEditor.AddNormalItem(project, sequence, "New inspection");

        Assert.Equal(2, edit.Sequence.Items.Count);
        Assert.Equal(2, edit.Item.Order);
        Assert.Equal("New inspection", edit.Item.Name);
        Assert.Equal(TestItemType.Normal, edit.Item.Type);
        Assert.True(edit.Item.Enabled);
        Assert.True(edit.Item.IsRequired);
        var rule = Assert.Single(edit.Item.Rules);
        Assert.Equal(ConfigurationTestFactory.TargetId, rule.TargetId);
        Assert.Equal(ConfigurationTestFactory.BindingId, rule.ModelBindingId);
        Assert.Equal(RegionType.FullImage, rule.Scope.Type);
        Assert.Equal(QuantityMetric.PresentCount, rule.Metric);
        Assert.Equal(ComparisonOperator.Equal, rule.Operator);
        Assert.Equal(1, rule.Threshold);
        Assert.Equal(0.5, rule.ConfidenceThreshold);
        Assert.Equal(InspectionVerdict.Pass, rule.OutcomeWhenMatched);

        var updatedProject = project with { TestSequences = [edit.Sequence] };
        var issues = ProjectConfigurationValidator.Validate(updatedProject);
        Assert.DoesNotContain(issues, issue => issue.Severity == ConfigurationValidationSeverity.Error);
    }

    [Fact]
    public void AddNormalItem_UsesPresenceForClassificationBinding()
    {
        var project = ConfigurationTestFactory.Create(ModelTaskType.Classification);
        var sequence = project.TestSequences[0] with { Items = [] };

        var edit = TestSequenceEditor.AddNormalItem(project, sequence, "Classification inspection");

        Assert.Equal(QuantityMetric.Presence, Assert.Single(edit.Item.Rules).Metric);
        var updatedProject = project with { TestSequences = [edit.Sequence] };
        var issues = ProjectConfigurationValidator.Validate(updatedProject);
        Assert.DoesNotContain(issues, issue => issue.Severity == ConfigurationValidationSeverity.Error);
    }

    [Fact]
    public void AddNormalItem_RejectsPoseOnlyBinding()
    {
        var project = ConfigurationTestFactory.Create(ModelTaskType.Pose);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            TestSequenceEditor.AddNormalItem(project, project.TestSequences[0], "New inspection"));

        Assert.Equal("请先创建带有效模型绑定的普通视觉目标。", exception.Message);
    }

    [Fact]
    public void RemoveItem_CompactsRemainingItemOrders()
    {
        var project = ConfigurationTestFactory.Create();
        var sequence = project.TestSequences[0];
        var second = TestSequenceEditor.AddNormalItem(project, sequence, "Second inspection");
        var third = TestSequenceEditor.AddNormalItem(project, second.Sequence, "Third inspection");

        var updated = TestSequenceEditor.RemoveItem(third.Sequence, second.Item.Id);

        Assert.Collection(
            updated.Items,
            item => Assert.Equal(1, item.Order),
            item =>
            {
                Assert.Equal(third.Item.Id, item.Id);
                Assert.Equal(2, item.Order);
            });
    }

    [Fact]
    public void RemoveItem_RejectsDeletingOnlySequenceItem()
    {
        var sequence = ConfigurationTestFactory.Create().TestSequences[0];

        var exception = Assert.Throws<InvalidOperationException>(() =>
            TestSequenceEditor.RemoveItem(sequence, sequence.Items[0].Id));

        Assert.Equal("测试序列至少需要一个测试项。", exception.Message);
    }
}
