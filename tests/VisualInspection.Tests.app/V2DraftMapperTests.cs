using System.Collections.ObjectModel;
using VisualInspection.App;
using VisualInspection.App.ViewModels.V2;
using VisualInspection.Core.V2.Configuration;

namespace VisualInspection.App.Tests;

public sealed class V2DraftMapperTests
{
    [Fact]
    public void RoundTrip_DoesNotInventCustomFunctionForImportedStep()
    {
        var model = CreateModel("fan", 0, KnownAdapterIds.YoloEndToEndDetection, ["fan"]);
        var item = new TestSequenceWizardV2Window.InspectionItemPreview("TS-FAN", "风扇", 0, true, model);
        var original = V2DraftMapper.ToProject(new TestSequenceWizardV2ViewModel([model], [item]));
        original = original with
        {
            TestStepCatalog = original.TestStepCatalog.Select(step => step with { CustomFunction = null }).ToList()
        };
        var legacy = ProjectConfigurationV2CompatibilityConverter.ToV1(original, System.IO.Path.GetTempPath());
        var editor = new TestSequenceWizardV2ViewModel([], []);
        V2DraftMapper.ApplyProject(editor, ProjectConfigurationV1Migrator.Migrate(legacy));

        Assert.Empty(editor.InspectionItems.Single().CustomFunctionName);
        Assert.Empty(editor.InspectionItems.Single().CustomFunctionFilePath);
        Assert.Null(V2DraftMapper.ToProject(editor).TestStepCatalog.Single().CustomFunction);
    }

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
    public void RoundTrip_PreservesRoiCoordinatesAndImportedImageReferenceSize()
    {
        var model = CreateModel("fan", 0, KnownAdapterIds.YoloEndToEndDetection, ["fan"]);
        var item = new TestSequenceWizardV2Window.InspectionItemPreview("TS-FAN", "风扇", 0, true, model)
        {
            TargetLabel = "fan",
            UseRoi = true,
            RoiRect = new System.Windows.Rect(571, 428, 2856, 2142),
            RoiReferenceWidth = 5712,
            RoiReferenceHeight = 4284
        };
        var source = new TestSequenceWizardV2ViewModel(
            new ObservableCollection<TestSequenceWizardV2Window.ModelPreview> { model },
            new ObservableCollection<TestSequenceWizardV2Window.InspectionItemPreview> { item });
        var project = V2DraftMapper.ToProject(source);
        var target = new TestSequenceWizardV2ViewModel(
            new ObservableCollection<TestSequenceWizardV2Window.ModelPreview>(),
            new ObservableCollection<TestSequenceWizardV2Window.InspectionItemPreview>());

        V2DraftMapper.ApplyProject(target, project);

        var mappedRoi = Assert.Single(Assert.Single(project.TestStepCatalog).RuleSet!.Rules).Scope.Regions.Single();
        var restored = Assert.Single(target.InspectionItems);
        Assert.Equal(5712, mappedRoi.ReferenceWidth);
        Assert.Equal(4284, mappedRoi.ReferenceHeight);
        Assert.Equal(item.RoiRect, restored.RoiRect);
        Assert.Equal(5712, restored.RoiReferenceWidth);
        Assert.Equal(4284, restored.RoiReferenceHeight);
        Assert.Equal(restored.RoiRect, restored.NamedRois.Single().Rect);
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
        Assert.Equal(InputSourceKind.VideoFolder, mappedSource.Kind);
        Assert.Equal("视频文件夹", mappedSource.Name);
        Assert.Equal(3, target.SourceKindIndex);
        Assert.Equal("视频文件夹", target.SourceKindLabel);
        Assert.Equal(@"C:\检测视频\Fan", target.SourceAddress);
    }

    [Fact]
    public void RoundTrip_PreservesIndependentScopesMissingBaselineAndFunctionMetadataThroughOperator()
    {
        var model = CreateModel("fan", 0, KnownAdapterIds.YoloEndToEndDetection, ["fan", "wire", "defect"]);
        var item = new TestSequenceWizardV2Window.InspectionItemPreview("TS-FAN", "风扇", 0, true, model)
        {
            RuleMetricIndex = 1, ExpectedTotalText = "4", ExpectedCountText = "0",
            CustomFunctionTypeIndex = 2, CustomFunctionName = "check_fan",
            CustomFunctionFilePath = "functions/check.py", CustomFunctionDescription = "保存元数据",
            CustomFunctionDelayMsText = "321"
        };
        var roi = new RegionScopeDefinitionV2
        {
            Type = RegionScopeTypeV2.Roi,
            Regions = [new RegionOfInterestV2 { Name = "wireROI", X1 = 100, Y1 = 80, X2 = 300, Y2 = 200,
                ReferenceWidth = 5712, ReferenceHeight = 4284 }]
        };
        item.AdditionalRules.Add(new RulePreviewViewModel("wire", model)
        { Scope = roi, MetricIndex = 1, ExpectedTotalText = "3", ThresholdText = "1" });
        item.AdditionalRules.Add(new RulePreviewViewModel("defect", model) { ThresholdText = "0" });
        var editor = new TestSequenceWizardV2ViewModel([model], [item]);
        var original = V2DraftMapper.ToProject(editor);
        var legacy = ProjectConfigurationV2CompatibilityConverter.ToV1(original, System.IO.Path.GetTempPath());
        var migrated = ProjectConfigurationV1Migrator.Migrate(legacy);
        var restoredEditor = new TestSequenceWizardV2ViewModel([], []);
        V2DraftMapper.ApplyProject(restoredEditor, migrated);
        var restored = V2DraftMapper.ToProject(restoredEditor).TestStepCatalog.Single();
        Assert.Equal(3, restoredEditor.InspectionItems.Single().DetectionChildren.Count);
        Assert.Equal(original.TestStepCatalog.Single().CustomFunction, restored.CustomFunction);
        Assert.Equal("321", restoredEditor.InspectionItems.Single().CustomFunctionDelayMsText);
        Assert.Equal([RegionScopeTypeV2.FullImage, RegionScopeTypeV2.Roi, RegionScopeTypeV2.FullImage],
            restored.RuleSet!.Rules.Select(rule => rule.Scope.Type));
        Assert.Equal(roi.Regions.Single(), restored.RuleSet.Rules[1].Scope.Regions.Single());
        Assert.Equal([4, 3, (int?)null], restored.RuleSet.Rules.Select(rule => rule.ExpectedCount));
        Assert.Equal([0, 1, 0], restored.RuleSet.Rules.Select(rule => rule.Threshold));
        var countRule = new VisualInspection.Core.Rules.CountRule("fan",
            VisualInspection.Core.Rules.QuantityMetric.MissingCount,
            VisualInspection.Core.Rules.ComparisonOperator.Equal,
            restored.RuleSet.Rules[0].Threshold, ExpectedCount: restored.RuleSet.Rules[0].ExpectedCount);
        Assert.Equal(VisualInspection.Core.Domain.InspectionVerdict.Fail,
            VisualInspection.Core.Rules.CountRuleEvaluator.Evaluate(countRule, 3).Verdict);
        Assert.Equal(VisualInspection.Core.Domain.InspectionVerdict.Pass,
            VisualInspection.Core.Rules.CountRuleEvaluator.Evaluate(countRule, 4).Verdict);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Operator_RejectsBothCurrentAndLegacyVideoDrafts(bool legacy)
    {
        var model = CreateModel("fan", 0, KnownAdapterIds.YoloEndToEndDetection, ["fan"]);
        var editor = new TestSequenceWizardV2ViewModel([model],
            [new TestSequenceWizardV2Window.InspectionItemPreview("TS-FAN", "风扇", 0, true, model)])
        { SourceKindIndex = 3 };
        var project = V2DraftMapper.ToProject(editor);
        if (legacy) project = project with
        { InputSourceDefinitions = [project.InputSourceDefinitions.Single() with { Kind = InputSourceKind.Folder }] };
        var exception = Assert.Throws<NotSupportedException>(() =>
            ProjectConfigurationV2CompatibilityConverter.ToV1(project, System.IO.Path.GetTempPath()));
        Assert.Contains("视频", exception.Message);
    }

    [Fact]
    public void OverlayLabelPlacement_AvoidsExistingLabelsAndStaysInImage()
    {
        var occupied = new List<System.Windows.Rect>();
        for (var index = 0; index < 8; index++)
        {
            var next = VisualInspection.App.ViewModels.MainWindowViewModel.PlaceOverlayLabel(
                620, 0, 180, 32, 640, 360, occupied);
            Assert.DoesNotContain(occupied, rectangle => rectangle.IntersectsWith(next));
            Assert.True(new System.Windows.Rect(0, 0, 640, 360).Contains(next));
            occupied.Add(next);
        }
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
