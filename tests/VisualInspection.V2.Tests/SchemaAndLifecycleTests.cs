using Legacy = VisualInspection.Core.Configuration;
using LegacyDomain = VisualInspection.Core.Domain;
using LegacyRules = VisualInspection.Core.Rules;
using VisualInspection.Core.V2.Configuration;
using VisualInspection.Infrastructure.V2.Persistence;

namespace VisualInspection.V2.Tests;

public sealed class SchemaAndLifecycleTests
{
    [Fact]
    public void ValidProject_PassesSchemaValidation()
    {
        var project = V2TestFactory.CreateProject(stepCount: 2, reverseCatalog: true);

        var issues = ProjectConfigurationV2Validator.Validate(project);

        Assert.DoesNotContain(issues, issue => issue.Severity == V2ValidationSeverity.Error);
    }

    [Fact]
    public void CatalogOrder_DoesNotChangeInvocationOrder()
    {
        var project = V2TestFactory.CreateProject(stepCount: 2, reverseCatalog: true);
        var sequence = project.TestSequenceVersions[0];

        Assert.NotEqual(project.TestStepCatalog[0].StepId, sequence.OrderedInvocations[0].StepId);
        Assert.Equal([1, 2], sequence.OrderedInvocations.Select(invocation => invocation.Order));
    }

    [Fact]
    public void SameStep_CanBeReferencedByMultipleSequencesAndRepeatedInOneSequence()
    {
        var project = V2TestFactory.CreateProject();
        var step = project.TestStepCatalog[0];
        var first = project.TestSequenceVersions[0] with
        {
            OrderedInvocations =
            [
                new StepInvocation { StepId = step.StepId, Order = 1 },
                new StepInvocation { StepId = step.StepId, Order = 2 }
            ]
        };
        var second = first with
        {
            SequenceId = Guid.NewGuid(),
            Version = "2.0.0",
            OrderedInvocations = [new StepInvocation { StepId = step.StepId, Order = 1 }]
        };
        project.TestSequenceVersions.Clear();
        project.TestSequenceVersions.AddRange([first, second]);

        var issues = ProjectConfigurationV2Validator.Validate(project);

        Assert.DoesNotContain(issues, issue => issue.Severity == V2ValidationSeverity.Error);
        Assert.Equal(step.StepId, first.OrderedInvocations[0].StepId);
        Assert.Equal(step.StepId, first.OrderedInvocations[1].StepId);
        Assert.Equal(step.StepId, second.OrderedInvocations[0].StepId);
    }

    [Fact]
    public void DuplicateFunctionCode_IsBlockedCaseInsensitively()
    {
        var project = V2TestFactory.CreateProject(stepCount: 2);
        project.TestStepCatalog[1] = project.TestStepCatalog[1] with
        {
            FunctionCode = project.TestStepCatalog[0].FunctionCode.ToLowerInvariant()
        };

        var issues = ProjectConfigurationV2Validator.Validate(project);

        Assert.Contains(issues, issue => issue.Code == "V2-FUNCTION-002");
    }

    [Fact]
    public void Rename_PreservesFunctionCode_AndDeleteRetiresIt()
    {
        var project = V2TestFactory.CreateProject();
        var catalog = new FunctionCodeCatalog(project);
        var step = project.TestStepCatalog[0];

        var renamed = catalog.Rename(step.StepId, "重命名后的步骤");
        catalog.Remove(step.StepId);

        Assert.Equal(step.FunctionCode, renamed.FunctionCode);
        Assert.Contains(step.FunctionCode, project.RetiredFunctionCodes);
        Assert.Throws<InvalidOperationException>(() =>
            catalog.Add(step.FunctionCode, "尝试复用", TestStepKind.Detection));
    }

    [Fact]
    public void PublishedReference_BlocksFunctionCodeMutation()
    {
        var project = V2TestFactory.CreateProject();
        var sequence = project.TestSequenceVersions[0];
        project.TestSequenceVersions[0] = sequence with
        {
            IsPublished = true,
            PublishedAtUtc = DateTimeOffset.UtcNow,
            ContentHash = new string('b', 64)
        };
        var catalog = new FunctionCodeCatalog(project);

        Assert.Throws<InvalidOperationException>(() =>
            catalog.ChangeFunctionCode(project.TestStepCatalog[0].StepId, "NEW_CODE"));
    }

    [Fact]
    public async Task Draft_RoundTripsStableIdsAndFunctionCode()
    {
        using var directory = new TemporaryDirectory();
        var store = new JsonConfigurationLifecycleStore(directory.Path);
        var service = new ConfigurationPublicationService(store, new NoIssueRuntimeValidator());
        var project = V2TestFactory.CreateProject();

        var saved = await service.SaveDraftAsync(project, "tester");
        var loaded = await service.LoadDraftAsync(saved.DraftId);

        Assert.NotNull(loaded);
        Assert.Equal(project.ProjectId, loaded.Project.ProjectId);
        Assert.Equal(project.TestStepCatalog[0].StepId, loaded.Project.TestStepCatalog[0].StepId);
        Assert.Equal(project.TestStepCatalog[0].FunctionCode, loaded.Project.TestStepCatalog[0].FunctionCode);
        Assert.Equal(project.TestSequenceVersions[0].OrderedInvocations[0].InvocationId,
            loaded.Project.TestSequenceVersions[0].OrderedInvocations[0].InvocationId);
    }

    [Fact]
    public async Task Publish_CreatesImmutablePackage_AndBlocksOverwrite()
    {
        using var directory = new TemporaryDirectory();
        var store = new JsonConfigurationLifecycleStore(directory.Path);
        var service = new ConfigurationPublicationService(store, new NoIssueRuntimeValidator());
        var project = V2TestFactory.CreateProject();
        var sequence = project.TestSequenceVersions[0];
        var deployment = project.DeploymentBindings[0];
        var draft = await service.SaveDraftAsync(project, "tester");

        var package = await service.PublishAsync(
            draft,
            sequence.SequenceId,
            sequence.Version,
            new RuntimeValidationContext
            {
                EnvironmentMode = RuntimeEnvironmentMode.Acceptance,
                DeploymentBindingId = deployment.DeploymentBindingId,
                BaseDirectory = directory.Path
            },
            "publisher");

        Assert.True(package.SequenceVersion.IsPublished);
        Assert.Equal(64, package.ContentHash.Length);
        Assert.Equal(
            ConfigurationPublicationService.CreatePackageId(sequence.SequenceId, sequence.Version),
            package.PackageId);
        var publishedDraft = await store.LoadDraftAsync(draft.DraftId);
        Assert.NotNull(publishedDraft);
        Assert.Equal(ConfigurationLifecycleStage.Published, publishedDraft.Stage);
        Assert.True(publishedDraft.Project.TestSequenceVersions.Single().IsPublished);
        await Assert.ThrowsAsync<IOException>(() => store.SavePublishedPackageAsync(package));
        await Assert.ThrowsAsync<IOException>(() => service.PublishAsync(
            draft,
            sequence.SequenceId,
            sequence.Version,
            new RuntimeValidationContext
            {
                EnvironmentMode = RuntimeEnvironmentMode.Acceptance,
                DeploymentBindingId = deployment.DeploymentBindingId,
                BaseDirectory = directory.Path
            },
            "publisher"));
    }

    [Fact]
    public void SchemaValidation_BlocksModelTaskTypeThatDoesNotMatchStepKind()
    {
        var project = V2TestFactory.CreateProject();
        project.ModelArtifacts[0] = project.ModelArtifacts[0] with
        {
            TaskType = TestStepKind.Classification
        };

        var issues = ProjectConfigurationV2Validator.Validate(project);

        Assert.Contains(issues, issue => issue.Code == "V2-BINDING-006");
    }

    [Fact]
    public async Task Rollback_OnlyMovesActivePointer_AndPreservesPackages()
    {
        using var directory = new TemporaryDirectory();
        var store = new JsonConfigurationLifecycleStore(directory.Path);
        var service = new ConfigurationPublicationService(store, new NoIssueRuntimeValidator());
        var project = V2TestFactory.CreateProject();
        var originalSequence = project.TestSequenceVersions[0];
        var deploymentId = project.DeploymentBindings[0].DeploymentBindingId;
        var firstDraft = await service.SaveDraftAsync(project, "tester");
        var first = await service.PublishAsync(
            firstDraft,
            originalSequence.SequenceId,
            originalSequence.Version,
            Context(deploymentId, directory.Path),
            "publisher");

        var nextProject = ConfigurationContentHasher.DeepClone(first.ProjectSnapshot);
        nextProject.TestSequenceVersions.Add(originalSequence with
        {
            Version = "2.0.0",
            IsPublished = false,
            PublishedAtUtc = null,
            ContentHash = null,
            OrderedInvocations = originalSequence.OrderedInvocations.Select(invocation => invocation with
            {
                InvocationId = Guid.NewGuid()
            }).ToList()
        });
        var secondDraft = await service.SaveDraftAsync(nextProject, "tester");
        var second = await service.PublishAsync(
            secondDraft,
            originalSequence.SequenceId,
            "2.0.0",
            Context(deploymentId, directory.Path),
            "publisher");

        await service.AssignAsync(first.PackageId, deploymentId, "admin");
        await service.ActivateAsync(first.PackageId, deploymentId, "admin");
        await service.AssignAsync(second.PackageId, deploymentId, "admin");
        await service.ActivateAsync(second.PackageId, deploymentId, "admin");
        var rollback = await service.RollbackAsync(deploymentId, first.PackageId, "admin");

        Assert.Equal(first.PackageId, rollback.PackageId);
        Assert.Equal(second.PackageId, rollback.PreviousPackageId);
        Assert.NotNull(await store.LoadPublishedPackageAsync(first.PackageId));
        Assert.NotNull(await store.LoadPublishedPackageAsync(second.PackageId));
    }

    [Fact]
    public void V1Migration_PreservesStableIdentityAndBuildsInvocationPlan()
    {
        var legacy = CreateLegacyProject();

        var migrated = ProjectConfigurationV1Migrator.Migrate(legacy);

        Assert.Equal(ConfigurationSchemaV2.CurrentVersion, migrated.SchemaVersion);
        Assert.Equal(legacy.Id, migrated.ProjectId);
        Assert.Single(migrated.TestStepCatalog);
        Assert.Single(migrated.TestSequenceVersions[0].OrderedInvocations);
        Assert.Equal(migrated.TestStepCatalog[0].StepId,
            migrated.TestSequenceVersions[0].OrderedInvocations[0].StepId);
        Assert.Equal(legacy.TestSequences[0].Items[0].Id, migrated.TestStepCatalog[0].StepId);
        Assert.Equal(legacy.Targets[0].ModelBindings[0].OutputLabelId,
            migrated.TestStepCatalog[0].RuleSet!.Rules[0].OutputLabelId);
        var deployment = Assert.Single(migrated.DeploymentBindings);
        var sourceBinding = Assert.Single(deployment.InputSourceBindings);
        Assert.Equal(legacy.InputSources[0].Id, sourceBinding.InputSourceId);
        Assert.Equal(legacy.InputSources[0].Id, sourceBinding.SourceBindingId);
        Assert.Equal(legacy.InputSources[0].Folder!.FolderPath, sourceBinding.DeviceAddress);
    }

    private static RuntimeValidationContext Context(Guid deploymentId, string baseDirectory) => new()
    {
        EnvironmentMode = RuntimeEnvironmentMode.Acceptance,
        DeploymentBindingId = deploymentId,
        BaseDirectory = baseDirectory
    };

    private static Legacy.ProjectConfiguration CreateLegacyProject()
    {
        var modelId = Guid.NewGuid();
        var bindingId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        return new Legacy.ProjectConfiguration
        {
            Name = "legacy",
            Workstation = "Station-1",
            Models =
            [
                new Legacy.ModelDefinition
                {
                    Id = modelId,
                    Name = "legacy-model",
                    Version = "1.0",
                    Format = Legacy.ModelFormat.Onnx,
                    TaskType = Legacy.ModelTaskType.Detection,
                    FilePath = "model.onnx",
                    Sha256 = new string('a', 64),
                    Labels = [new Legacy.ModelLabelDefinition { Id = 3, Name = "defect" }]
                }
            ],
            Targets =
            [
                new Legacy.TargetDefinition
                {
                    Id = targetId,
                    Name = "defect",
                    ModelBindings =
                    [
                        new Legacy.ModelBindingDefinition
                        {
                            Id = bindingId,
                            ModelId = modelId,
                            ModelVersion = "1.0",
                            OutputLabelId = 3
                        }
                    ]
                }
            ],
            InputSources =
            [
                new Legacy.InputSourceDefinition
                {
                    Id = sourceId,
                    Name = "folder",
                    Type = Legacy.InputSourceType.Folder,
                    Folder = new Legacy.FolderInputOptions { FolderPath = "images" }
                }
            ],
            TestSequences =
            [
                new Legacy.TestSequenceDefinition
                {
                    Name = "legacy-sequence",
                    Version = "1.0",
                    InputSourceId = sourceId,
                    Items =
                    [
                        new Legacy.TestItemDefinition
                        {
                            Id = Guid.NewGuid(),
                            Order = 1,
                            Name = "缺陷检测",
                            Type = Legacy.TestItemType.Normal,
                            Rules =
                            [
                                new Legacy.TargetRuleDefinition
                                {
                                    TargetId = targetId,
                                    ModelBindingId = bindingId,
                                    Metric = LegacyRules.QuantityMetric.PresentCount,
                                    Operator = LegacyRules.ComparisonOperator.GreaterThan,
                                    Threshold = 0,
                                    OutcomeWhenMatched = LegacyDomain.InspectionVerdict.Fail
                                }
                            ]
                        }
                    ]
                }
            ]
        };
    }
}
