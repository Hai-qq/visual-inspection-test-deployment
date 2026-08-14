using VisualInspection.Core.Configuration;
using VisualInspection.Core.Domain;
using VisualInspection.Core.Rules;

namespace VisualInspection.Core.Tests;

public sealed class ProjectConfigurationValidatorTests
{
    [Fact]
    public void Validate_AcceptsCompleteNormalSequence()
    {
        var issues = ProjectConfigurationValidator.Validate(ConfigurationTestFactory.Create());

        Assert.DoesNotContain(issues, issue => issue.Severity == ConfigurationValidationSeverity.Error);
    }

    [Fact]
    public void Validate_RejectsBindingToMissingModel()
    {
        var project = ConfigurationTestFactory.Create() with { Models = [] };

        var issues = ProjectConfigurationValidator.Validate(project);

        Assert.Contains(issues, issue => issue.Code == "CFG-BIND-002");
    }

    [Fact]
    public void Validate_RejectsRoiOutsideReferenceImage()
    {
        var project = ConfigurationTestFactory.Create();
        var sequence = project.TestSequences[0];
        var item = sequence.Items[0];
        var rule = item.Rules[0] with
        {
            Scope = new RegionScopeDefinition
            {
                Type = RegionType.Roi,
                Regions =
                [
                    new RegionOfInterestDefinition
                    {
                        Name = "ROI-A",
                        X1 = 10,
                        Y1 = 10,
                        X2 = 210,
                        Y2 = 90,
                        ReferenceWidth = 200,
                        ReferenceHeight = 100
                    }
                ]
            }
        };
        project = project with
        {
            TestSequences = [sequence with { Items = [item with { Rules = [rule] }] }]
        };

        var issues = ProjectConfigurationValidator.Validate(project);

        Assert.Contains(issues, issue => issue.Code == "CFG-ROI-005");
    }

    [Fact]
    public void Validate_RejectsInstanceCountForClassificationModel()
    {
        var issues = ProjectConfigurationValidator.Validate(
            ConfigurationTestFactory.Create(ModelTaskType.Classification));

        Assert.Contains(issues, issue => issue.Code == "CFG-RULE-005");
    }

    [Fact]
    public void Validate_RejectsMissingCountWithoutExpectedCount()
    {
        var project = ConfigurationTestFactory.Create();
        var sequence = project.TestSequences[0];
        var item = sequence.Items[0];
        var rule = item.Rules[0] with { Metric = QuantityMetric.MissingCount, ExpectedCount = null };
        project = project with
        {
            TestSequences = [sequence with { Items = [item with { Rules = [rule] }] }]
        };

        var issues = ProjectConfigurationValidator.Validate(project);

        Assert.Contains(issues, issue => issue.Code == "CFG-RULE-007");
    }

    [Fact]
    public void Validate_RejectsErrorAsBusinessRuleOutcome()
    {
        var project = ConfigurationTestFactory.Create();
        var sequence = project.TestSequences[0];
        var item = sequence.Items[0];
        var rule = item.Rules[0] with { OutcomeWhenMatched = InspectionVerdict.Error };
        project = project with
        {
            TestSequences = [sequence with { Items = [item with { Rules = [rule] }] }]
        };

        var issues = ProjectConfigurationValidator.Validate(project);

        Assert.Contains(issues, issue => issue.Code == "CFG-RULE-006");
    }

    [Fact]
    public void Validate_RejectsDuplicateTestItemOrder()
    {
        var project = ConfigurationTestFactory.Create();
        var sequence = project.TestSequences[0];
        var firstItem = sequence.Items[0];
        var secondItem = firstItem with { Id = Guid.NewGuid(), Name = "Second item" };
        project = project with
        {
            TestSequences = [sequence with { Items = [firstItem, secondItem] }]
        };

        var issues = ProjectConfigurationValidator.Validate(project);

        Assert.Contains(issues, issue => issue.Code == "CFG-ORDER-002");
    }

    [Fact]
    public void Validate_RejectsFolderSourceWithCameraOptions()
    {
        var project = ConfigurationTestFactory.Create();
        var source = project.InputSources[0] with
        {
            Camera = new CameraInputOptions
            {
                AdapterId = "directshow",
                DeviceId = "camera-1",
                Width = 1920,
                Height = 1080,
                FrameRate = 30
            }
        };
        project = project with { InputSources = [source] };

        var issues = ProjectConfigurationValidator.Validate(project);

        Assert.Contains(issues, issue => issue.Code == "CFG-SOURCE-003");
    }

    [Fact]
    public void Validate_RejectsPoseStepBoundToDetectionModel()
    {
        var project = ConfigurationTestFactory.Create();
        var sequence = project.TestSequences[0];
        var item = sequence.Items[0] with
        {
            Type = TestItemType.PoseSequence,
            Rules = [],
            PoseSteps =
            [
                new PoseStepDefinition
                {
                    Order = 1,
                    Name = "Pick",
                    ActionCondition = "hand_near_part",
                    ModelBindingId = ConfigurationTestFactory.BindingId,
                    MinimumHoldMs = 100,
                    MaximumWaitMs = 1000
                }
            ]
        };
        project = project with
        {
            TestSequences = [sequence with { Items = [item] }]
        };

        var issues = ProjectConfigurationValidator.Validate(project);

        Assert.Contains(issues, issue => issue.Code == "CFG-POSE-006");
    }

    [Fact]
    public void Validate_RejectsPublishedSequenceWhenUsedModelHasNoChecksum()
    {
        var project = ConfigurationTestFactory.Create();
        project = project with { Models = [project.Models[0] with { Sha256 = null }] };

        var issues = ProjectConfigurationValidator.Validate(project);

        Assert.Contains(issues, issue => issue.Code == "CFG-SEQ-006");
    }

    [Fact]
    public void Validate_RejectsDuplicatePublishedNameAndVersion()
    {
        var project = ConfigurationTestFactory.Create();
        var sequence = project.TestSequences[0];
        project = project with
        {
            TestSequences = [sequence, sequence with { Id = Guid.NewGuid() }]
        };

        var issues = ProjectConfigurationValidator.Validate(project);

        Assert.Contains(issues, issue => issue.Code == "CFG-SEQ-005");
    }
}
