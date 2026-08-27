using VisualInspection.Core.Imaging;

namespace VisualInspection.Core.V2.Configuration;

public static class ConfigurationSchemaV2
{
    public const int CurrentVersion = 2;
}

public static class KnownAdapterIds
{
    public const string YoloEndToEndDetection = "onnx-yolo-e2e-detection";
    public const string YoloRawDetection = "onnx-yolo-raw-detection";
    public const string Classification = "onnx-classification";
    public const string Segmentation = "onnx-segmentation";
    public const string PoseKeypoint = "onnx-pose-keypoint";
    public const string TemporalAction = "onnx-temporal-action";
    public const string Manifest = "manifest-deterministic";
    public const string UnconfiguredCamera = "camera-unconfigured";
    public const string SimulatedCamera = "camera-simulator";
    public const string UnconfiguredLineResult = "line-unconfigured";
    public const string SimulatedLineResult = "line-simulator";
}

public enum RuntimeEnvironmentMode
{
    Development,
    Acceptance,
    Production
}

public enum TestStepKind
{
    Detection,
    Classification,
    Segmentation,
    Pose,
    Temporal
}

public enum ModelArtifactFormat
{
    Onnx,
    Pt
}

public enum RuntimeExecutionProvider
{
    Cpu,
    Cuda,
    TensorRt
}

public enum InputSourceKind
{
    Folder,
    Camera,
    ExternalContext
}

public enum FrameInputPolicy
{
    CaptureOncePerProduct,
    CapturePerInvocation,
    ContinuousStream,
    ExternalContextFrame,
    OperatorDebugSelection
}

public enum CapturePolicy
{
    CaptureOncePerProduct,
    CapturePerInvocation,
    ContinuousStream,
    ExternalContextFrame
}

public enum InvocationFailurePolicy
{
    StopSequence,
    ContinueSequence
}

public enum TriggerCondition
{
    RisingEdge,
    FallingEdge,
    HighLevel,
    LowLevel
}

public enum QueueOverflowPolicy
{
    RejectNewest,
    DropOldest,
    Wait
}

public enum CaptureMode
{
    SingleFrame,
    Continuous
}

public enum RuleLogicalOperatorV2
{
    And,
    Or
}

public enum RuleMetricV2
{
    PresentCount,
    MissingCount,
    Presence
}

public enum RuleComparisonOperatorV2
{
    Equal,
    NotEqual,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual,
    BetweenInclusive
}

public enum RuleOutcome
{
    Pass,
    Fail
}

public enum RegionScopeTypeV2
{
    FullImage,
    Roi
}

public sealed record ProjectConfigurationV2
{
    public int SchemaVersion { get; init; } = ConfigurationSchemaV2.CurrentVersion;
    public Guid ProjectId { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = string.Empty;
    public string Workstation { get; init; } = string.Empty;
    public List<ModelArtifact> ModelArtifacts { get; init; } = [];
    public List<RuntimeProfile> RuntimeProfiles { get; init; } = [];
    public List<InputSourceDefinitionV2> InputSourceDefinitions { get; init; } = [];
    public List<TestStepDefinition> TestStepCatalog { get; init; } = [];
    public List<TestSequenceVersion> TestSequenceVersions { get; init; } = [];
    public List<DeploymentBinding> DeploymentBindings { get; init; } = [];
    public List<string> RetiredFunctionCodes { get; init; } = [];
}

public sealed record ModelArtifact
{
    public Guid ModelArtifactId { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public ModelArtifactFormat Format { get; init; }
    public TestStepKind TaskType { get; init; }
    public string? FilePath { get; init; }
    public string? ArtifactUri { get; init; }
    public string Sha256 { get; init; } = string.Empty;
    public List<ModelLabel> LabelSet { get; init; } = [];
    public string LabelSetVersion { get; init; } = string.Empty;
    public string AdapterId { get; init; } = string.Empty;
    public Guid RuntimeProfileId { get; init; }
}

public sealed record ModelLabel
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
}

public sealed record RuntimeProfile
{
    public Guid RuntimeProfileId { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = string.Empty;
    public RuntimeExecutionProvider ExecutionProvider { get; init; } = RuntimeExecutionProvider.Cpu;
    public int IntraOpThreads { get; init; } = 1;
    public int InterOpThreads { get; init; } = 1;
    public int WarmupCount { get; init; } = 1;
    public int MaxConcurrency { get; init; } = 1;
    public long MemoryBudgetBytes { get; init; } = 512L * 1024 * 1024;
    public int? DeviceId { get; init; }
}

public sealed record InputSourceDefinitionV2
{
    public Guid InputSourceId { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = string.Empty;
    public InputSourceKind Kind { get; init; }
    public Guid SourceBindingId { get; init; }
    public ImageFrameDataFormat? ExpectedDataFormat { get; init; }
    public int MaximumFrameAgeMs { get; init; } = 1000;
}

public sealed record TestStepDefinition
{
    public Guid StepId { get; init; } = Guid.NewGuid();
    public string FunctionCode { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public TestStepKind Kind { get; init; }
    public List<ModelBindingV2> ModelBindings { get; init; } = [];
    public RuleSetDefinition? RuleSet { get; init; }
    public PoseProgramDefinition? PoseProgram { get; init; }
    public InvocationPolicyDefinition InvocationPolicy { get; init; } = new();
    public int DefaultTimeoutMs { get; init; } = 5000;
    public FrameInputPolicy DefaultFrameInputPolicy { get; init; } = FrameInputPolicy.CaptureOncePerProduct;
}

public sealed record ModelBindingV2
{
    public Guid ModelBindingId { get; init; } = Guid.NewGuid();
    public Guid ModelArtifactId { get; init; }
    public string ModelVersion { get; init; } = string.Empty;
    public int OutputLabelId { get; init; }
    public string AdapterId { get; init; } = string.Empty;
    public Guid AdapterProfileId { get; init; }
}

public sealed record InvocationPolicyDefinition
{
    public bool AllowSequenceInvocation { get; init; } = true;
    public bool AllowManualDebugInvocation { get; init; }
    public List<ExternalTriggerBinding> ExternalTriggerBindings { get; init; } = [];
}

public sealed record ExternalTriggerBinding
{
    public Guid BindingId { get; init; } = Guid.NewGuid();
    public string LogicalSignalTag { get; init; } = string.Empty;
    public TriggerCondition TriggerCondition { get; init; }
    public int DebounceMs { get; init; }
    public int PostTriggerDelayMs { get; init; }
    public int TimeoutMs { get; init; } = 5000;
    public FrameInputPolicy FrameInputPolicy { get; init; } = FrameInputPolicy.CaptureOncePerProduct;
    public int MaxConcurrency { get; init; } = 1;
    public int QueueCapacity { get; init; } = 1;
    public QueueOverflowPolicy OverflowPolicy { get; init; } = QueueOverflowPolicy.RejectNewest;
}

public sealed record RuleSetDefinition
{
    public RuleLogicalOperatorV2 LogicalOperator { get; init; } = RuleLogicalOperatorV2.And;
    public List<InspectionRuleDefinition> Rules { get; init; } = [];
}

public sealed record InspectionRuleDefinition
{
    public Guid RuleId { get; init; } = Guid.NewGuid();
    public Guid ModelBindingId { get; init; }
    public int OutputLabelId { get; init; }
    public RuleMetricV2 Metric { get; init; }
    public RuleComparisonOperatorV2 Operator { get; init; }
    public int Threshold { get; init; }
    public int? UpperThreshold { get; init; }
    public int? ExpectedCount { get; init; }
    public double ConfidenceThreshold { get; init; } = 0.5;
    public RegionScopeDefinitionV2 Scope { get; init; } = new();
    public RuleOutcome OutcomeWhenMatched { get; init; } = RuleOutcome.Pass;
}

public sealed record RegionScopeDefinitionV2
{
    public RegionScopeTypeV2 Type { get; init; } = RegionScopeTypeV2.FullImage;
    public List<RegionOfInterestV2> Regions { get; init; } = [];
}

public sealed record RegionOfInterestV2
{
    public Guid RegionId { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = string.Empty;
    public int X1 { get; init; }
    public int Y1 { get; init; }
    public int X2 { get; init; }
    public int Y2 { get; init; }
    public int ReferenceWidth { get; init; }
    public int ReferenceHeight { get; init; }
}

public sealed record PoseProgramDefinition
{
    public int ProgramTimeoutMs { get; init; } = 30000;
    public int MaximumFrameGapMs { get; init; } = 500;
    public List<PoseActionDefinition> Actions { get; init; } = [];
}

public sealed record PoseActionDefinition
{
    public Guid ActionId { get; init; } = Guid.NewGuid();
    public int Order { get; init; }
    public string Name { get; init; } = string.Empty;
    public string ActionCondition { get; init; } = string.Empty;
    public Guid ModelBindingId { get; init; }
    public double ConfidenceThreshold { get; init; } = 0.5;
    public int MinimumHoldMs { get; init; }
    public int MaximumWaitMs { get; init; } = 5000;
    public bool IsRequired { get; init; } = true;
}

public sealed record TestSequenceVersion
{
    public Guid SequenceId { get; init; } = Guid.NewGuid();
    public string Version { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public bool IsPublished { get; init; }
    public DateTimeOffset? PublishedAtUtc { get; init; }
    public string? ContentHash { get; init; }
    public Guid DefaultSourceBindingId { get; init; }
    public List<StepInvocation> OrderedInvocations { get; init; } = [];
}

public sealed record StepInvocation
{
    public Guid InvocationId { get; init; } = Guid.NewGuid();
    public Guid StepId { get; init; }
    public int Order { get; init; }
    public bool IsRequired { get; init; } = true;
    public int DelayMs { get; init; }
    public int? TimeoutOverrideMs { get; init; }
    public CapturePolicy CapturePolicy { get; init; } = CapturePolicy.CaptureOncePerProduct;
    public InvocationFailurePolicy FailurePolicy { get; init; } = InvocationFailurePolicy.StopSequence;
    public Guid? SourceBindingId { get; init; }
}

public sealed record DeploymentBinding
{
    public Guid DeploymentBindingId { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = string.Empty;
    public string StationId { get; init; } = string.Empty;
    public List<TriggerSignalDeploymentBinding> TriggerSignalBindings { get; init; } = [];
    public List<InputSourceDeploymentBinding> InputSourceBindings { get; init; } = [];
    public LineResultDeploymentBinding? LineResultBinding { get; init; }
    public List<string> AdapterAllowlist { get; init; } = [];
}

public sealed record TriggerSignalDeploymentBinding
{
    public Guid BindingId { get; init; } = Guid.NewGuid();
    public string LogicalSignalTag { get; init; } = string.Empty;
    public string TriggerAdapterId { get; init; } = string.Empty;
    public string Protocol { get; init; } = string.Empty;
    public string ConnectionProfileId { get; init; } = string.Empty;
    public string DeviceAddress { get; init; } = string.Empty;
}

public sealed record InputSourceDeploymentBinding
{
    public Guid SourceBindingId { get; init; }
    public Guid InputSourceId { get; init; }
    public string CameraAdapterId { get; init; } = string.Empty;
    public string ConnectionProfileId { get; init; } = string.Empty;
    public string DeviceAddress { get; init; } = string.Empty;
}

public sealed record LineResultDeploymentBinding
{
    public string AdapterId { get; init; } = string.Empty;
    public string ConnectionProfileId { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, string> LogicalPointMap { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
