using VisualInspection.App.Demo;
using VisualInspection.Core.Configuration;
using VisualInspection.Core.Domain;
using VisualInspection.Core.Rules;
using VisualInspection.Core.V2.Configuration;

namespace VisualInspection.App.Tests;

public sealed class SampleProjectFactoryTests
{
    [Fact]
    public void Create_LoadsPublishedFanSingleImageDemo()
    {
        const string fanFolder = @"C:\demo\fan-pass";

        const string fanModel = @"C:\demo\models\fan.onnx";

        var project = SampleProjectFactory.Create(fanFolder, fanModel);
        var sequence = Assert.Single(project.TestSequences);
        var source = Assert.Single(project.InputSources);

        Assert.Equal(SampleProjectFactory.SampleProjectName, project.Name);
        Assert.Equal(SampleProjectFactory.SampleProductModel, sequence.Name);
        Assert.True(sequence.IsPublished);
        Assert.Equal(fanFolder, source.Folder?.FolderPath);
        var fanItem = Assert.Single(sequence.Items);
        Assert.Equal("风扇检测", fanItem.Name);
        Assert.Equal(TestItemType.Normal, fanItem.Type);
        Assert.Equal(RuleLogicalOperator.And, fanItem.RuleOperator);
        Assert.Equal(6, fanItem.Rules.Count);
        Assert.Equal(
            ["标签", "黑线", "白线", "反向标签", "反向黑线", "反向白线"],
            fanItem.Rules.Select(rule => project.Targets.Single(target => target.Id == rule.TargetId).Name));
        Assert.Equal([3, 3, 1, 0, 0, 0], fanItem.Rules.Select(rule => rule.Threshold));
        Assert.All(fanItem.Rules.Take(3), rule =>
        {
            Assert.Equal(ComparisonOperator.Equal, rule.Operator);
            Assert.Equal(InspectionVerdict.Pass, rule.OutcomeWhenMatched);
        });
        Assert.All(fanItem.Rules.Skip(3), rule =>
        {
            Assert.Equal(ComparisonOperator.GreaterThan, rule.Operator);
            Assert.Equal(InspectionVerdict.Fail, rule.OutcomeWhenMatched);
        });
        var model = Assert.Single(project.Models);
        Assert.Equal(SampleProjectFactory.SampleModelName, model.Name);
        Assert.Equal(fanModel, model.FilePath);
        Assert.Equal(SampleProjectFactory.BundledFanModelSha256, model.Sha256);
        Assert.Equal(
            ["Labell", "Black_wire", "white_wire", "reverse_Labell", "reverse_Black_wire", "reverse_white_wire"],
            model.Labels.OrderBy(label => label.Id).Select(label => label.Name));
    }

    [Fact]
    public void Create_MigratesToSchemaValidPortableSequence()
    {
        var project = SampleProjectFactory.Create(
            @"C:\demo\input",
            @"C:\demo\models\fan.onnx");

        var migrated = ProjectConfigurationV1Migrator.Migrate(project);
        var errors = ProjectConfigurationV2Validator.Validate(migrated)
            .Where(issue => issue.Severity == V2ValidationSeverity.Error)
            .ToArray();

        Assert.True(
            errors.Length == 0,
            string.Join(
                Environment.NewLine,
                migrated.TestStepCatalog.Select(step => $"FunctionCode={step.FunctionCode}")
                    .Concat(errors.Select(error => $"{error.Code}: {error.Path}: {error.Message}"))));
    }

    [Fact]
    public void Create_UsesStableTestStepAndRuleIdentities()
    {
        var first = SampleProjectFactory.Create(@"C:\demo\input", @"C:\demo\models\fan.onnx");
        var second = SampleProjectFactory.Create(@"C:\demo\input", @"C:\demo\models\fan.onnx");

        var firstItem = Assert.Single(Assert.Single(first.TestSequences).Items);
        var secondItem = Assert.Single(Assert.Single(second.TestSequences).Items);
        Assert.Equal(firstItem.Id, secondItem.Id);
        Assert.Equal(firstItem.Rules.Select(rule => rule.Id), secondItem.Rules.Select(rule => rule.Id));

        var firstMigrated = ProjectConfigurationV1Migrator.Migrate(first);
        var secondMigrated = ProjectConfigurationV1Migrator.Migrate(second);
        Assert.Equal(
            Assert.Single(firstMigrated.TestStepCatalog).FunctionCode,
            Assert.Single(secondMigrated.TestStepCatalog).FunctionCode);
    }
}
