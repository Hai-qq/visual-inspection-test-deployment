using VisualInspection.Core.V2.Configuration;
using VisualInspection.Core.V2.Execution;
using VisualInspection.Infrastructure.V2.Adapters;
using VisualInspection.Infrastructure.V2.Runtime;
using VisualInspection.Runner;

namespace VisualInspection.V2.Tests;

public sealed class ProductionPolicyTests
{
    [Fact]
    public async Task Acceptance_AllowsDeterministicManifestAdapter()
    {
        using var directory = new TemporaryDirectory();
        var setup = CreateManifestSetup(directory.Path);
        var issues = await setup.Validator.ValidateAsync(
            setup.Project,
            setup.Project.TestSequenceVersions[0],
            Context(setup.Project, RuntimeEnvironmentMode.Acceptance, directory.Path));

        Assert.DoesNotContain(issues, issue => issue.Severity == V2ValidationSeverity.Error);
    }

    [Fact]
    public async Task Production_RejectsManifestAndSimulatorAdapters()
    {
        using var directory = new TemporaryDirectory();
        var setup = CreateManifestSetup(directory.Path);

        var issues = await setup.Validator.ValidateAsync(
            setup.Project,
            setup.Project.TestSequenceVersions[0],
            Context(setup.Project, RuntimeEnvironmentMode.Production, directory.Path));

        Assert.Contains(issues, issue => issue.Code == "V2-PROD-ADAPTER" && issue.Path.Contains("modelBindings"));
        Assert.Contains(issues, issue => issue.Code == "V2-PROD-ADAPTER" && issue.Path.Contains("inputSourceBindings"));
        Assert.Contains(issues, issue => issue.Code == "V2-PROD-ADAPTER" && issue.Path.Contains("lineResultBinding"));
    }

    [Fact]
    public async Task ProductionWithOnlyDetectionsJson_RemainsNotReadyAndCannotPass()
    {
        using var directory = new TemporaryDirectory();
        var setup = CreateManifestSetup(directory.Path);
        var readinessGate = new SequenceReadinessGate(
            setup.Validator,
            setup.ModelRegistry,
            directory.Path);
        await using var orchestrator = new SequenceOrchestrator(
            readinessGate,
            setup.ModelRegistry,
            setup.CameraRegistry,
            setup.LineRegistry,
            new SequenceOrchestratorOptions { BaseDirectory = directory.Path });
        setup.Camera.Enqueue(V2TestFactory.CreateFrame());

        var request = V2TestFactory.CreateRunRequest(
            setup.Project,
            RuntimeEnvironmentMode.Production);
        var result = await orchestrator.EnqueueAsync(request);
        var line = await setup.Line.GetSnapshotAsync();

        Assert.Equal(RunState.Errored, result.State);
        Assert.Equal(BusinessVerdict.NotEvaluated, result.Verdict);
        Assert.Equal(RuntimeEnvironmentMode.Production, result.TestRecord.EnvironmentMode);
        Assert.Contains(result.TestRecord.Providers, provider =>
            provider.ProviderKind == RuntimeProviderKind.DeterministicManifest);
        Assert.Equal(0, setup.ModelExecutions);
        Assert.Equal(LineHandshakeState.NotReady, line.State);
        Assert.False(line.Ready);
        Assert.False(line.Pass);
    }

    [Fact]
    public async Task AcceptanceManifest_RunRecordsProviderAndCanExecuteDeterministically()
    {
        using var directory = new TemporaryDirectory();
        var setup = CreateManifestSetup(directory.Path);
        var readinessGate = new SequenceReadinessGate(setup.Validator, setup.ModelRegistry, directory.Path);
        await using var orchestrator = new SequenceOrchestrator(
            readinessGate,
            setup.ModelRegistry,
            setup.CameraRegistry,
            setup.LineRegistry,
            new SequenceOrchestratorOptions { BaseDirectory = directory.Path });
        setup.Camera.Enqueue(V2TestFactory.CreateFrame());

        var result = await orchestrator.EnqueueAsync(V2TestFactory.CreateRunRequest(
            setup.Project,
            RuntimeEnvironmentMode.Acceptance));

        Assert.Equal(RunState.Completed, result.State);
        Assert.Equal(BusinessVerdict.Pass, result.Verdict);
        Assert.Equal(RuntimeEnvironmentMode.Acceptance, result.TestRecord.EnvironmentMode);
        Assert.Contains(result.TestRecord.Providers, provider =>
            provider.ProviderKind == RuntimeProviderKind.DeterministicManifest);
        Assert.Equal(1, setup.ModelExecutions);
    }

    [Fact]
    public async Task MissingModelFile_MakesRuntimeNotReady()
    {
        using var directory = new TemporaryDirectory();
        var setup = CreateManifestSetup(directory.Path);
        setup.Project.ModelArtifacts[0] = setup.Project.ModelArtifacts[0] with
        {
            FilePath = Path.Combine(directory.Path, "missing.onnx")
        };

        var issues = await setup.Validator.ValidateAsync(
            setup.Project,
            setup.Project.TestSequenceVersions[0],
            Context(setup.Project, RuntimeEnvironmentMode.Acceptance, directory.Path));

        Assert.Contains(issues, issue => issue.Code == "V2-RUNTIME-MODEL-FILE");
    }

    [Fact]
    public async Task ProductionDebugSourceSelection_IsRejected()
    {
        using var directory = new TemporaryDirectory();
        var setup = CreateManifestSetup(directory.Path);
        setup.Project.TestStepCatalog[0] = setup.Project.TestStepCatalog[0] with
        {
            DefaultFrameInputPolicy = FrameInputPolicy.OperatorDebugSelection
        };

        var issues = await setup.Validator.ValidateAsync(
            setup.Project,
            setup.Project.TestSequenceVersions[0],
            Context(setup.Project, RuntimeEnvironmentMode.Production, directory.Path));

        Assert.Contains(issues, issue => issue.Code == "V2-PROD-DEBUG-SOURCE");
    }

    private static ManifestSetup CreateManifestSetup(string directory)
    {
        var manifestPath = Path.Combine(directory, "detections.json");
        File.WriteAllText(manifestPath, "{\"schemaVersion\":1,\"frames\":[]}");
        var project = V2TestFactory.CreateProject(
            adapterId: KnownAdapterIds.Manifest,
            modelPath: manifestPath,
            modelHash: V2TestFactory.Sha256(manifestPath));
        var executions = new Counter();
        var model = new SimulatedModelRuntimeAdapter(
            KnownAdapterIds.Manifest,
            TestStepKind.Detection,
            (request, _) =>
            {
                executions.Value++;
                return Task.FromResult(new ModelObservation
                {
                    CaptureId = request.Frame.CaptureId,
                    ModelBindingId = request.Binding.ModelBindingId,
                    ObservedAtUtc = DateTimeOffset.UtcNow,
                    Counts = new Dictionary<ModelOutputKey, int>
                    {
                        [new ModelOutputKey(request.Binding.ModelBindingId, request.Binding.OutputLabelId)] = 0
                    }
                });
            },
            providerKind: RuntimeProviderKind.DeterministicManifest);
        var camera = new SimulatedCameraAdapter();
        var line = new SimulatedLineResultAdapter();
        var modelRegistry = new ModelRuntimeAdapterRegistry([model]);
        var cameraRegistry = new CameraAdapterRegistry([camera]);
        var lineRegistry = new LineResultAdapterRegistry([line]);
        var validator = new RuntimeConfigurationValidator(
            modelRegistry,
            cameraRegistry,
            new TriggerAdapterRegistry([]),
            lineRegistry);
        return new ManifestSetup(
            project,
            modelRegistry,
            cameraRegistry,
            lineRegistry,
            validator,
            camera,
            line,
            executions);
    }

    private static RuntimeValidationContext Context(
        ProjectConfigurationV2 project,
        RuntimeEnvironmentMode mode,
        string baseDirectory) => new()
        {
            EnvironmentMode = mode,
            BaseDirectory = baseDirectory,
            DeploymentBindingId = project.DeploymentBindings[0].DeploymentBindingId
        };

    private sealed record ManifestSetup(
        ProjectConfigurationV2 Project,
        ModelRuntimeAdapterRegistry ModelRegistry,
        CameraAdapterRegistry CameraRegistry,
        LineResultAdapterRegistry LineRegistry,
        RuntimeConfigurationValidator Validator,
        SimulatedCameraAdapter Camera,
        SimulatedLineResultAdapter Line,
        Counter Executions)
    {
        public int ModelExecutions => Executions.Value;
    }

    private sealed class Counter
    {
        public int Value { get; set; }
    }
}
