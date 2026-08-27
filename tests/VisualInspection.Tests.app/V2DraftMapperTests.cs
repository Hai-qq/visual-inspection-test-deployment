using System.Collections.ObjectModel;
using VisualInspection.App;
using VisualInspection.App.ViewModels.V2;
using VisualInspection.Core.V2.Configuration;

namespace VisualInspection.App.Tests;

public sealed class V2DraftMapperTests
{
    [Fact]
    public void ToProject_UsesTestStepListOrderAndAllowsMultipleInvocationChannels()
    {
        var model = CreateModel("fan", 0, KnownAdapterIds.YoloEndToEndDetection, ["fan", "defect"]);
        var first = new TestSequenceWizardV2Window.InspectionItemPreview("TS-FAN", "风扇", 0, true, model)
        {
            AllowSequenceInvocation = true,
            ExternalTriggerEnabled = true,
            TriggerSignal = "PLC.Line1.PartPresent",
            TargetLabel = "fan"
        };
        var second = new TestSequenceWizardV2Window.InspectionItemPreview("TS-DEFECT", "缺陷", 0, false, model)
        {
            AllowSequenceInvocation = true,
            TargetLabel = "defect",
            RuleOutcomeIndex = 1
        };
        var editor = new TestSequenceWizardV2ViewModel(
            new ObservableCollection<TestSequenceWizardV2Window.ModelPreview> { model },
            new ObservableCollection<TestSequenceWizardV2Window.InspectionItemPreview> { first, second });

        Assert.True(editor.MoveStep(second, -1));
        var project = V2DraftMapper.ToProject(editor);

        Assert.Empty(ProjectConfigurationV2Validator.Validate(project)
            .Where(issue => issue.Severity == V2ValidationSeverity.Error));
        Assert.Equal(["TS-DEFECT", "TS-FAN"], project.TestSequenceVersions.Single().OrderedInvocations
            .Select(invocation => project.TestStepCatalog.Single(step => step.StepId == invocation.StepId).FunctionCode));
        Assert.Equal(project.TestStepCatalog.Count, project.TestSequenceVersions.Single().OrderedInvocations.Count);
        Assert.Equal(project.TestStepCatalog.Count, project.TestSequenceVersions.Single().OrderedInvocations
            .Select(invocation => invocation.StepId).Distinct().Count());
        var mappedFirst = project.TestStepCatalog.Single(step => step.FunctionCode == "TS-FAN");
        Assert.True(mappedFirst.InvocationPolicy.AllowSequenceInvocation);
        Assert.Single(mappedFirst.InvocationPolicy.ExternalTriggerBindings);
    }

    [Fact]
    public void ToProject_PreservesPerActionPoseSettingsAndModelBindings()
    {
        var model = CreateModel("pose", 1, KnownAdapterIds.TemporalAction, ["pick", "place"]);
        var item = new TestSequenceWizardV2Window.InspectionItemPreview(
            "TS-POSE",
            "取放",
            1,
            true,
            model,
            ["pick", "place"]);
        item.PoseSteps[0].MinimumHoldMsText = "120";
        item.PoseSteps[0].MaximumWaitMsText = "2000";
        item.PoseSteps[0].ConfidenceThresholdText = "0.60";
        item.PoseSteps[1].MinimumHoldMsText = "450";
        item.PoseSteps[1].MaximumWaitMsText = "6000";
        item.PoseSteps[1].ConfidenceThresholdText = "0.85";
        var editor = new TestSequenceWizardV2ViewModel(
            new ObservableCollection<TestSequenceWizardV2Window.ModelPreview> { model },
            new ObservableCollection<TestSequenceWizardV2Window.InspectionItemPreview> { item });

        var step = V2DraftMapper.ToProject(editor).TestStepCatalog.Single();

        Assert.Equal(2, step.ModelBindings.Count);
        Assert.Equal([120, 450], step.PoseProgram!.Actions.Select(action => action.MinimumHoldMs));
        Assert.Equal([2000, 6000], step.PoseProgram.Actions.Select(action => action.MaximumWaitMs));
        Assert.Equal([0.60, 0.85], step.PoseProgram.Actions.Select(action => action.ConfidenceThreshold));
    }

    [Fact]
    public void ToProject_EmitsExactlyOneInvocationPerTestStep()
    {
        var model = CreateModel("fan", 0, KnownAdapterIds.YoloEndToEndDetection, ["fan"]);
        var first = new TestSequenceWizardV2Window.InspectionItemPreview("TS-FAN", "风扇", 0, true, model);
        var second = new TestSequenceWizardV2Window.InspectionItemPreview("TS-HOUSING", "外壳", 0, false, model);
        var editor = new TestSequenceWizardV2ViewModel(
            new ObservableCollection<TestSequenceWizardV2Window.ModelPreview> { model },
            new ObservableCollection<TestSequenceWizardV2Window.InspectionItemPreview> { first, second });

        var invocations = V2DraftMapper.ToProject(editor).TestSequenceVersions.Single().OrderedInvocations;

        Assert.Equal(2, invocations.Count);
        Assert.Equal([first.StepId, second.StepId], invocations.Select(invocation => invocation.StepId));
        Assert.Equal([1, 2], invocations.Select(invocation => invocation.Order));
        Assert.Equal([first.InvocationId, second.InvocationId], invocations.Select(invocation => invocation.InvocationId));
        Assert.NotEqual(invocations[0].InvocationId, invocations[1].InvocationId);
    }

    [Fact]
    public void RoundTrip_PreservesStableLabelIdsAndPerRuleModelBindings()
    {
        var primaryModel = CreateModel(
            "fan",
            0,
            KnownAdapterIds.YoloEndToEndDetection,
            ["fan", "defect"],
            [7, 42]);
        var secondaryModel = CreateModel(
            "surface",
            0,
            KnownAdapterIds.YoloEndToEndDetection,
            ["scratch"],
            [99]);
        var item = new TestSequenceWizardV2Window.InspectionItemPreview("TS-FAN", "风扇", 0, true, primaryModel)
        {
            TargetLabel = "defect"
        };
        item.AdditionalRules.Add(new RulePreviewViewModel("scratch", secondaryModel)
        {
            OutcomeIndex = 1
        });
        var source = new TestSequenceWizardV2ViewModel(
            new ObservableCollection<TestSequenceWizardV2Window.ModelPreview> { primaryModel, secondaryModel },
            new ObservableCollection<TestSequenceWizardV2Window.InspectionItemPreview> { item });
        var project = V2DraftMapper.ToProject(source);
        var target = new TestSequenceWizardV2ViewModel(
            new ObservableCollection<TestSequenceWizardV2Window.ModelPreview>(),
            new ObservableCollection<TestSequenceWizardV2Window.InspectionItemPreview>());

        V2DraftMapper.ApplyProject(target, project);
        var restored = V2DraftMapper.ToProject(target);

        Assert.Equal(
            project.ModelArtifacts.SelectMany(artifact => artifact.LabelSet)
                .Select(label => (label.Id, label.Name)).OrderBy(label => label.Id),
            restored.ModelArtifacts.SelectMany(artifact => artifact.LabelSet)
                .Select(label => (label.Id, label.Name)).OrderBy(label => label.Id));
        var restoredStep = restored.TestStepCatalog.Single();
        Assert.Equal([42, 99], restoredStep.ModelBindings.Select(binding => binding.OutputLabelId));
        Assert.Equal(
            [primaryModel.ModelArtifactId, secondaryModel.ModelArtifactId],
            restoredStep.ModelBindings.Select(binding => binding.ModelArtifactId));
        Assert.Equal([42, 99], restoredStep.RuleSet!.Rules.Select(rule => rule.OutputLabelId));
    }

    [Fact]
    public void ApplyProject_RestoresStableIdsAndInvocationOrder()
    {
        var model = CreateModel("fan", 0, KnownAdapterIds.YoloEndToEndDetection, ["fan"]);
        var first = new TestSequenceWizardV2Window.InspectionItemPreview("TS-FAN", "风扇", 0, true, model);
        var second = new TestSequenceWizardV2Window.InspectionItemPreview("TS-HOUSING", "外壳", 0, false, model);
        var source = new TestSequenceWizardV2ViewModel(
            new ObservableCollection<TestSequenceWizardV2Window.ModelPreview> { model },
            new ObservableCollection<TestSequenceWizardV2Window.InspectionItemPreview> { first, second });
        Assert.True(source.MoveStep(second, -1));
        var project = V2DraftMapper.ToProject(source);
        var target = new TestSequenceWizardV2ViewModel(
            new ObservableCollection<TestSequenceWizardV2Window.ModelPreview>(),
            new ObservableCollection<TestSequenceWizardV2Window.InspectionItemPreview>());

        V2DraftMapper.ApplyProject(target, project);
        var restored = V2DraftMapper.ToProject(target);

        Assert.Equal(project.ProjectId, restored.ProjectId);
        Assert.Equal(project.ModelArtifacts.Single().ModelArtifactId, restored.ModelArtifacts.Single().ModelArtifactId);
        Assert.Equal(["TS-HOUSING", "TS-FAN"], target.InspectionItems.Select(item => item.FunctionCode));
        Assert.Equal(
            project.TestSequenceVersions.Single().OrderedInvocations.Select(invocation => invocation.StepId),
            restored.TestSequenceVersions.Single().OrderedInvocations.Select(invocation => invocation.StepId));
        Assert.Equal(
            project.TestSequenceVersions.Single().OrderedInvocations.Select(invocation => invocation.InvocationId),
            restored.TestSequenceVersions.Single().OrderedInvocations.Select(invocation => invocation.InvocationId));
    }

    [Fact]
    public void ApplyProject_RestoresSegmentationAsDistinctSingleFrameUiType()
    {
        var model = CreateModel("surface", 3, KnownAdapterIds.Segmentation, ["coating", "background"]);
        var item = new TestSequenceWizardV2Window.InspectionItemPreview(
            "TS-SEGMENT",
            "涂层区域分割",
            2,
            true,
            model)
        {
            TargetLabel = "coating"
        };
        var source = new TestSequenceWizardV2ViewModel(
            new ObservableCollection<TestSequenceWizardV2Window.ModelPreview> { model },
            new ObservableCollection<TestSequenceWizardV2Window.InspectionItemPreview> { item });
        var project = V2DraftMapper.ToProject(source);
        var target = new TestSequenceWizardV2ViewModel(
            new ObservableCollection<TestSequenceWizardV2Window.ModelPreview>(),
            new ObservableCollection<TestSequenceWizardV2Window.InspectionItemPreview>());

        V2DraftMapper.ApplyProject(target, project);

        var restoredItem = Assert.Single(target.InspectionItems);
        Assert.Equal(TestStepKind.Segmentation, Assert.Single(project.TestStepCatalog).Kind);
        Assert.Equal(2, restoredItem.TypeIndex);
        Assert.Equal("图像分割", restoredItem.TypeLabel);
        Assert.Equal(3, restoredItem.Model.TypeIndex);
        Assert.NotEmpty(restoredItem.DetectionChildren);
    }

    [Fact]
    public void RoundTrip_PreservesVideoFolderAsDistinctFrontendSource()
    {
        var source = new TestSequenceWizardV2ViewModel(
            new ObservableCollection<TestSequenceWizardV2Window.ModelPreview>(),
            new ObservableCollection<TestSequenceWizardV2Window.InspectionItemPreview>())
        {
            SourceKindIndex = 3,
            SourceAddress = @"C:\检测视频\Fan"
        };
        var project = V2DraftMapper.ToProject(source);
        var target = new TestSequenceWizardV2ViewModel(
            new ObservableCollection<TestSequenceWizardV2Window.ModelPreview>(),
            new ObservableCollection<TestSequenceWizardV2Window.InspectionItemPreview>());

        V2DraftMapper.ApplyProject(target, project);

        var mappedSource = Assert.Single(project.InputSourceDefinitions);
        Assert.Equal(InputSourceKind.Folder, mappedSource.Kind);
        Assert.Equal("视频文件夹", mappedSource.Name);
        Assert.Equal(3, target.SourceKindIndex);
        Assert.Equal("视频文件夹", target.SourceKindLabel);
        Assert.Equal(@"C:\检测视频\Fan", target.SourceAddress);
    }

    private static TestSequenceWizardV2Window.ModelPreview CreateModel(
        string name,
        int typeIndex,
        string adapterId,
        IEnumerable<string> labels,
        IEnumerable<int>? labelIds = null) =>
        new(name, $"{name}.onnx", typeIndex, false, labels, labelIds: labelIds)
        {
            Version = "1.0.0",
            Sha256 = new string('a', 64),
            AdapterId = adapterId,
            LabelSetVersion = "1"
        };
}
