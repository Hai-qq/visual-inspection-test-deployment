using VisualInspection.Core.Domain;
using VisualInspection.Core.Rules;

namespace VisualInspection.Core.Configuration;

public static class ConfigurationSchema
{
    public const int CurrentVersion = 1;
}

public sealed record ProjectConfiguration
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = string.Empty;
    public string Workstation { get; init; } = string.Empty;
    public List<ModelDefinition> Models { get; init; } = [];
    public List<TargetDefinition> Targets { get; init; } = [];
    public List<InputSourceDefinition> InputSources { get; init; } = [];
    public List<TestSequenceDefinition> TestSequences { get; init; } = [];
}

public sealed record ModelDefinition
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public ModelFormat Format { get; init; }
    public ModelTaskType TaskType { get; init; }
    public string FilePath { get; init; } = string.Empty;
    public string? Sha256 { get; init; }
    public LabelSourceMode LabelSource { get; init; }
    public List<ModelLabelDefinition> Labels { get; init; } = [];
}

public sealed record ModelLabelDefinition
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
}

public sealed record TargetDefinition
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = string.Empty;
    public List<ModelBindingDefinition> ModelBindings { get; init; } = [];
}

public sealed record ModelBindingDefinition
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid ModelId { get; init; }
    public string ModelVersion { get; init; } = string.Empty;
    public int OutputLabelId { get; init; }
}

public sealed record InputSourceDefinition
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = string.Empty;
    public InputSourceType Type { get; init; }
    public FolderInputOptions? Folder { get; init; }
    public CameraInputOptions? Camera { get; init; }
}

public sealed record FolderInputOptions
{
    public string FolderPath { get; init; } = string.Empty;
    public bool IncludeSubfolders { get; init; }
    public FolderSortOrder SortOrder { get; init; } = FolderSortOrder.NaturalFileName;
    public InvalidFileBehavior InvalidFileBehavior { get; init; } = InvalidFileBehavior.Skip;
    public bool LoopPlayback { get; init; }
    public int PoseFrameIntervalMs { get; init; } = 33;
}

public sealed record CameraInputOptions
{
    public string AdapterId { get; init; } = string.Empty;
    public string DeviceId { get; init; } = string.Empty;
    public int Width { get; init; }
    public int Height { get; init; }
    public double FrameRate { get; init; }
    public string PixelFormat { get; init; } = string.Empty;
    public string TriggerMode { get; init; } = string.Empty;
    public int GrabTimeoutMs { get; init; } = 1000;
}

public sealed record TestSequenceDefinition
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public int DefaultDelayMs { get; init; }
    public Guid InputSourceId { get; init; }
    public RuntimeSourcePolicy SourcePolicy { get; init; } = RuntimeSourcePolicy.Fixed;
    public bool IsPublished { get; init; }
    public DateTimeOffset? PublishedAtUtc { get; init; }
    public List<TestItemDefinition> Items { get; init; } = [];
}

public sealed record TestItemDefinition
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public int Order { get; init; }
    public string Name { get; init; } = string.Empty;
    public TestItemType Type { get; init; }
    public bool Enabled { get; init; } = true;
    public bool IsRequired { get; init; } = true;
    public int? DelayMs { get; init; }
    public RuleLogicalOperator RuleOperator { get; init; } = RuleLogicalOperator.And;
    public List<TargetRuleDefinition> Rules { get; init; } = [];
    public List<PoseStepDefinition> PoseSteps { get; init; } = [];
}

public sealed record TargetRuleDefinition
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TargetId { get; init; }
    public Guid ModelBindingId { get; init; }
    public RegionScopeDefinition Scope { get; init; } = new();
    public QuantityMetric Metric { get; init; }
    public ComparisonOperator Operator { get; init; }
    public int Threshold { get; init; }
    public int? UpperThreshold { get; init; }
    public int? ExpectedCount { get; init; }
    public double ConfidenceThreshold { get; init; } = 0.5;
    public InspectionVerdict OutcomeWhenMatched { get; init; } = InspectionVerdict.Pass;
}

public sealed record RegionScopeDefinition
{
    public RegionType Type { get; init; } = RegionType.FullImage;
    public List<RegionOfInterestDefinition> Regions { get; init; } = [];
}

public sealed record RegionOfInterestDefinition
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = string.Empty;
    public int X1 { get; init; }
    public int Y1 { get; init; }
    public int X2 { get; init; }
    public int Y2 { get; init; }
    public int ReferenceWidth { get; init; }
    public int ReferenceHeight { get; init; }
}

public sealed record PoseStepDefinition
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public int Order { get; init; }
    public string Name { get; init; } = string.Empty;
    public string ActionCondition { get; init; } = string.Empty;
    public Guid ModelBindingId { get; init; }
    public double ConfidenceThreshold { get; init; } = 0.5;
    public int MinimumHoldMs { get; init; }
    public int MaximumWaitMs { get; init; }
    public bool IsRequired { get; init; } = true;
}
