namespace VisualInspection.Core.V2.Configuration;

public enum ConfigurationLifecycleStage
{
    Draft,
    SchemaValidated,
    RuntimeValidated,
    Published,
    Assigned,
    Active
}

public enum V2ValidationSeverity
{
    Warning,
    Error
}

public sealed record V2ValidationIssue(
    V2ValidationSeverity Severity,
    string Code,
    string Path,
    string Message);

public sealed record RuntimeValidationContext
{
    public RuntimeEnvironmentMode EnvironmentMode { get; init; }
    public string BaseDirectory { get; init; } = string.Empty;
    public Guid? DeploymentBindingId { get; init; }
}

public sealed record ConfigurationValidationReport
{
    public required IReadOnlyList<V2ValidationIssue> SchemaIssues { get; init; }
    public required IReadOnlyList<V2ValidationIssue> RuntimeIssues { get; init; }

    public bool SchemaValidated => SchemaIssues.All(issue => issue.Severity != V2ValidationSeverity.Error);
    public bool RuntimeValidated => SchemaValidated && RuntimeIssues.All(issue => issue.Severity != V2ValidationSeverity.Error);
    public bool CanPublish => SchemaValidated && RuntimeValidated;
}

public sealed record ConfigurationDraftV2
{
    public Guid DraftId { get; init; } = Guid.NewGuid();
    public required ProjectConfigurationV2 Project { get; init; }
    public DateTimeOffset SavedAtUtc { get; init; }
    public string SavedBy { get; init; } = string.Empty;
    public ConfigurationLifecycleStage Stage { get; init; } = ConfigurationLifecycleStage.Draft;
    public ConfigurationValidationReport? LastValidation { get; init; }
}

public sealed record PublishedConfigurationPackage
{
    public Guid PackageId { get; init; } = Guid.NewGuid();
    public int SchemaVersion { get; init; } = ConfigurationSchemaV2.CurrentVersion;
    public required ProjectConfigurationV2 ProjectSnapshot { get; init; }
    public required TestSequenceVersion SequenceVersion { get; init; }
    public required IReadOnlyList<ModelArtifactReference> ModelArtifactReferences { get; init; }
    public string ContentHash { get; init; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; init; }
    public string CreatedBy { get; init; } = string.Empty;
}

public sealed record ModelArtifactReference(
    Guid ModelArtifactId,
    string Version,
    string Sha256,
    string AdapterId,
    Guid RuntimeProfileId);

public sealed record DeploymentAssignment
{
    public required Guid DeploymentBindingId { get; init; }
    public required Guid PackageId { get; init; }
    public DateTimeOffset AssignedAtUtc { get; init; }
    public string AssignedBy { get; init; } = string.Empty;
}

public sealed record ActiveDeploymentPointer
{
    public required Guid DeploymentBindingId { get; init; }
    public required Guid PackageId { get; init; }
    public Guid? PreviousPackageId { get; init; }
    public DateTimeOffset ActivatedAtUtc { get; init; }
    public string ActivatedBy { get; init; } = string.Empty;
}

public interface IRuntimeConfigurationValidator
{
    Task<IReadOnlyList<V2ValidationIssue>> ValidateAsync(
        ProjectConfigurationV2 project,
        TestSequenceVersion sequence,
        RuntimeValidationContext context,
        CancellationToken cancellationToken = default);
}

public interface IConfigurationLifecycleStore
{
    Task SaveDraftAsync(ConfigurationDraftV2 draft, CancellationToken cancellationToken = default);
    Task<ConfigurationDraftV2?> LoadDraftAsync(Guid draftId, CancellationToken cancellationToken = default);
    Task SavePublishedPackageAsync(PublishedConfigurationPackage package, CancellationToken cancellationToken = default);
    Task<PublishedConfigurationPackage?> LoadPublishedPackageAsync(Guid packageId, CancellationToken cancellationToken = default);
    Task SaveAssignmentAsync(DeploymentAssignment assignment, CancellationToken cancellationToken = default);
    Task<DeploymentAssignment?> LoadAssignmentAsync(Guid deploymentBindingId, CancellationToken cancellationToken = default);
    Task SaveActivePointerAsync(ActiveDeploymentPointer pointer, CancellationToken cancellationToken = default);
    Task<ActiveDeploymentPointer?> LoadActivePointerAsync(Guid deploymentBindingId, CancellationToken cancellationToken = default);
}
