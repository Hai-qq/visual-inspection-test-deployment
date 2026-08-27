using System.Security.Cryptography;
using VisualInspection.Core.Imaging;
using VisualInspection.Core.V2.Configuration;
using VisualInspection.Core.V2.Execution;

namespace VisualInspection.V2.Tests;

internal static class V2TestFactory
{
    public static ProjectConfigurationV2 CreateProject(
        int stepCount = 1,
        string adapterId = "model-simulator",
        string modelPath = "model.onnx",
        string? modelHash = null,
        bool includeExternalTrigger = false,
        bool reverseCatalog = false)
    {
        var projectId = Guid.NewGuid();
        var artifactId = Guid.NewGuid();
        var profileId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var sourceBindingId = Guid.NewGuid();
        var deploymentId = Guid.NewGuid();
        var steps = Enumerable.Range(1, stepCount).Select(index =>
        {
            var bindingId = Guid.NewGuid();
            return new TestStepDefinition
            {
                StepId = Guid.NewGuid(),
                FunctionCode = $"STEP_{index:00}",
                Name = $"检测步骤 {index}",
                Kind = TestStepKind.Detection,
                ModelBindings =
                [
                    new ModelBindingV2
                    {
                        ModelBindingId = bindingId,
                        ModelArtifactId = artifactId,
                        ModelVersion = "1.0.0",
                        OutputLabelId = 0,
                        AdapterId = adapterId,
                        AdapterProfileId = profileId
                    }
                ],
                RuleSet = new RuleSetDefinition
                {
                    Rules =
                    [
                        new InspectionRuleDefinition
                        {
                            ModelBindingId = bindingId,
                            OutputLabelId = 0,
                            Metric = RuleMetricV2.PresentCount,
                            Operator = RuleComparisonOperatorV2.GreaterThan,
                            Threshold = 0,
                            OutcomeWhenMatched = RuleOutcome.Fail,
                            Scope = new RegionScopeDefinitionV2()
                        }
                    ]
                },
                InvocationPolicy = new InvocationPolicyDefinition
                {
                    AllowSequenceInvocation = true,
                    AllowManualDebugInvocation = true,
                    ExternalTriggerBindings = includeExternalTrigger
                        ?
                        [
                            new ExternalTriggerBinding
                            {
                                LogicalSignalTag = "Line.PartReady",
                                TriggerCondition = TriggerCondition.RisingEdge,
                                DebounceMs = 10,
                                TimeoutMs = 1000,
                                QueueCapacity = 2,
                                MaxConcurrency = 1
                            }
                        ]
                        : []
                },
                DefaultTimeoutMs = 1000,
                DefaultFrameInputPolicy = FrameInputPolicy.CaptureOncePerProduct
            };
        }).ToList();
        var invocationSteps = steps.ToArray();
        var catalog = reverseCatalog ? steps.AsEnumerable().Reverse().ToList() : steps;
        var sequence = new TestSequenceVersion
        {
            SequenceId = Guid.NewGuid(),
            Version = "1.0.0",
            Name = "主测试序列",
            DefaultSourceBindingId = sourceBindingId,
            OrderedInvocations = invocationSteps.Select((step, index) => new StepInvocation
            {
                StepId = step.StepId,
                Order = index + 1,
                IsRequired = true,
                CapturePolicy = CapturePolicy.CaptureOncePerProduct,
                FailurePolicy = InvocationFailurePolicy.StopSequence
            }).ToList()
        };
        return new ProjectConfigurationV2
        {
            ProjectId = projectId,
            Name = "V2 测试项目",
            Workstation = "Station-1",
            RuntimeProfiles =
            [
                new RuntimeProfile
                {
                    RuntimeProfileId = profileId,
                    Name = "CPU",
                    ExecutionProvider = RuntimeExecutionProvider.Cpu,
                    IntraOpThreads = 1,
                    InterOpThreads = 1,
                    WarmupCount = 0,
                    MaxConcurrency = 1,
                    MemoryBudgetBytes = 1024 * 1024
                }
            ],
            ModelArtifacts =
            [
                new ModelArtifact
                {
                    ModelArtifactId = artifactId,
                    Name = "测试模型",
                    Version = "1.0.0",
                    Format = ModelArtifactFormat.Onnx,
                    TaskType = TestStepKind.Detection,
                    FilePath = modelPath,
                    Sha256 = modelHash ?? new string('a', 64),
                    LabelSet = [new ModelLabel { Id = 0, Name = "defect" }],
                    LabelSetVersion = "1.0.0",
                    AdapterId = adapterId,
                    RuntimeProfileId = profileId
                }
            ],
            InputSourceDefinitions =
            [
                new InputSourceDefinitionV2
                {
                    InputSourceId = sourceId,
                    SourceBindingId = sourceBindingId,
                    Name = "模拟相机",
                    Kind = InputSourceKind.Camera,
                    ExpectedDataFormat = ImageFrameDataFormat.Gray8,
                    MaximumFrameAgeMs = 2000
                }
            ],
            TestStepCatalog = catalog,
            TestSequenceVersions = [sequence],
            DeploymentBindings =
            [
                new DeploymentBinding
                {
                    DeploymentBindingId = deploymentId,
                    Name = "测试 Deployment",
                    StationId = "Station-1",
                    AdapterAllowlist = [adapterId, KnownAdapterIds.SimulatedCamera, KnownAdapterIds.SimulatedLineResult, "trigger-simulator"],
                    InputSourceBindings =
                    [
                        new InputSourceDeploymentBinding
                        {
                            SourceBindingId = sourceBindingId,
                            InputSourceId = sourceId,
                            CameraAdapterId = KnownAdapterIds.SimulatedCamera,
                            ConnectionProfileId = "sim-camera",
                            DeviceAddress = "SIM-1"
                        }
                    ],
                    TriggerSignalBindings = includeExternalTrigger
                        ?
                        [
                            new TriggerSignalDeploymentBinding
                            {
                                LogicalSignalTag = "Line.PartReady",
                                TriggerAdapterId = "trigger-simulator",
                                Protocol = "Simulator",
                                ConnectionProfileId = "sim-trigger",
                                DeviceAddress = "SIM.Trigger.1"
                            }
                        ]
                        : [],
                    LineResultBinding = new LineResultDeploymentBinding
                    {
                        AdapterId = KnownAdapterIds.SimulatedLineResult,
                        ConnectionProfileId = "sim-line",
                        LogicalPointMap = new Dictionary<string, string>
                        {
                            ["Ready"] = "SIM.Ready",
                            ["Busy"] = "SIM.Busy",
                            ["ResultValid"] = "SIM.ResultValid",
                            ["Pass"] = "SIM.Pass",
                            ["Fail"] = "SIM.Fail",
                            ["Error"] = "SIM.Error",
                            ["Heartbeat"] = "SIM.Heartbeat",
                            ["Ack"] = "SIM.Ack",
                            ["ResultSequenceNumber"] = "SIM.Sequence"
                        }
                    }
                }
            ]
        };
    }

    public static SequenceRunRequest CreateRunRequest(
        ProjectConfigurationV2 project,
        RuntimeEnvironmentMode environmentMode = RuntimeEnvironmentMode.Acceptance,
        string? idempotencyKey = null,
        long sequenceNumber = 1,
        TimeSpan? deadline = null)
    {
        var sequence = project.TestSequenceVersions[0];
        var deployment = project.DeploymentBindings[0];
        var now = DateTimeOffset.UtcNow;
        return new SequenceRunRequest
        {
            Project = project,
            SequenceId = sequence.SequenceId,
            SequenceVersion = sequence.Version,
            DeploymentBindingId = deployment.DeploymentBindingId,
            EnvironmentMode = environmentMode,
            DeadlineUtc = now + (deadline ?? TimeSpan.FromSeconds(5)),
            Trigger = new TriggerEvent
            {
                SignalTag = "Sequence",
                OccurredAtUtc = now,
                ReceivedAtUtc = now,
                ProductId = $"P-{Guid.NewGuid():N}",
                StationId = deployment.StationId,
                SequenceNumber = sequenceNumber,
                IdempotencyKey = idempotencyKey ?? Guid.NewGuid().ToString("N")
            }
        };
    }

    public static FrameEnvelope CreateFrame(
        DateTimeOffset? hardwareTimestamp = null,
        long frameCounter = 1,
        FrameQualityFlags qualityFlags = FrameQualityFlags.None)
    {
        var timestamp = hardwareTimestamp ?? DateTimeOffset.UtcNow;
        return new FrameEnvelope
        {
            ImageFrame = new ImageFrame
            {
                SourceId = Guid.NewGuid(),
                SequenceNumber = frameCounter,
                CapturedAtUtc = timestamp,
                Width = 1,
                Height = 1,
                DataFormat = ImageFrameDataFormat.Gray8,
                Data = new byte[] { 128 },
                Stride = 1,
                Origin = "simulated"
            },
            CaptureId = Guid.NewGuid(),
            TriggerEventId = Guid.NewGuid(),
            ProductId = "template",
            StationId = "Station-1",
            HardwareTimestampUtc = timestamp,
            ReceivedAtUtc = timestamp,
            FrameCounter = frameCounter,
            QualityFlags = qualityFlags
        };
    }

    public static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}

internal sealed class NoIssueRuntimeValidator : IRuntimeConfigurationValidator
{
    public Task<IReadOnlyList<V2ValidationIssue>> ValidateAsync(
        ProjectConfigurationV2 project,
        TestSequenceVersion sequence,
        RuntimeValidationContext context,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<V2ValidationIssue>>([]);
}

internal sealed class AlwaysReadyGate(IReadOnlyList<ProviderRecord>? providers = null) : ISequenceReadinessGate
{
    private readonly IReadOnlyList<ProviderRecord> _providers = providers ??
    [
        new ProviderRecord("model-simulator", RuntimeProviderKind.Simulator, true)
    ];

    public Task<SequenceReadinessResult> EvaluateAsync(
        ProjectConfigurationV2 project,
        TestSequenceVersion sequence,
        DeploymentBinding deployment,
        RuntimeEnvironmentMode environmentMode,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new SequenceReadinessResult
        {
            IsReady = true,
            Issues = [],
            Providers = _providers
        });
}

internal sealed class TemporaryDirectory : IDisposable
{
    public TemporaryDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "VisualInspectionV2Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
