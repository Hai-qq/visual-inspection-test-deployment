using System.Security.Cryptography;
using System.Text;
using VisualInspection.Core.V2.Configuration;

namespace VisualInspection.Infrastructure.V2.Persistence;

public sealed class ConfigurationPublishException(
    string message,
    ConfigurationValidationReport validationReport) : InvalidOperationException(message)
{
    public ConfigurationValidationReport ValidationReport { get; } = validationReport;
}

public sealed class ConfigurationPublicationService(
    IConfigurationLifecycleStore store,
    IRuntimeConfigurationValidator runtimeValidator,
    TimeProvider? timeProvider = null)
{
    private readonly IConfigurationLifecycleStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly IRuntimeConfigurationValidator _runtimeValidator =
        runtimeValidator ?? throw new ArgumentNullException(nameof(runtimeValidator));
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<ConfigurationDraftV2> SaveDraftAsync(
        ProjectConfigurationV2 project,
        string savedBy,
        Guid? draftId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(savedBy);
        var draft = new ConfigurationDraftV2
        {
            DraftId = draftId ?? Guid.NewGuid(),
            Project = ConfigurationContentHasher.DeepClone(project),
            SavedAtUtc = _timeProvider.GetUtcNow(),
            SavedBy = savedBy,
            Stage = ConfigurationLifecycleStage.Draft
        };
        await _store.SaveDraftAsync(draft, cancellationToken);
        return draft;
    }

    public Task<ConfigurationDraftV2?> LoadDraftAsync(
        Guid draftId,
        CancellationToken cancellationToken = default) =>
        _store.LoadDraftAsync(draftId, cancellationToken);

    public async Task<ConfigurationDraftV2> ValidateAsync(
        ConfigurationDraftV2 draft,
        Guid sequenceId,
        string sequenceVersion,
        RuntimeValidationContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentException.ThrowIfNullOrWhiteSpace(sequenceVersion);
        var schemaIssues = ProjectConfigurationV2Validator.Validate(draft.Project);
        var sequence = ResolveSequence(draft.Project, sequenceId, sequenceVersion);
        var runtimeIssues = ProjectConfigurationV2Validator.HasErrors(schemaIssues)
            ? Array.Empty<V2ValidationIssue>()
            : await _runtimeValidator.ValidateAsync(draft.Project, sequence, context, cancellationToken);
        var report = new ConfigurationValidationReport
        {
            SchemaIssues = schemaIssues,
            RuntimeIssues = runtimeIssues
        };
        var stage = report.RuntimeValidated && report.SchemaValidated
            ? ConfigurationLifecycleStage.RuntimeValidated
            : report.SchemaValidated
                ? ConfigurationLifecycleStage.SchemaValidated
                : ConfigurationLifecycleStage.Draft;
        var validated = draft with
        {
            Project = ConfigurationContentHasher.DeepClone(draft.Project),
            SavedAtUtc = _timeProvider.GetUtcNow(),
            Stage = stage,
            LastValidation = report
        };
        await _store.SaveDraftAsync(validated, cancellationToken);
        return validated;
    }

    public async Task<PublishedConfigurationPackage> PublishAsync(
        ConfigurationDraftV2 draft,
        Guid sequenceId,
        string sequenceVersion,
        RuntimeValidationContext context,
        string createdBy,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(createdBy);
        var validated = await ValidateAsync(draft, sequenceId, sequenceVersion, context, cancellationToken);
        if (validated.LastValidation is null || !validated.LastValidation.CanPublish)
        {
            throw new ConfigurationPublishException("配置未同时通过 Schema 和 Runtime Validation，禁止发布。", validated.LastValidation!);
        }

        var createdAt = _timeProvider.GetUtcNow();
        var projectSnapshot = ConfigurationContentHasher.DeepClone(validated.Project);
        var sequence = ResolveSequence(projectSnapshot, sequenceId, sequenceVersion);
        if (sequence.IsPublished)
        {
            throw new InvalidOperationException("该 Sequence Version 已发布，必须创建新版本，不能覆盖。");
        }

        var modelReferences = ResolveModelReferences(projectSnapshot, sequence);
        var contentHash = ConfigurationContentHasher.Compute(projectSnapshot, sequence, modelReferences);
        var publishedSequence = sequence with
        {
            IsPublished = true,
            PublishedAtUtc = createdAt,
            ContentHash = contentHash
        };
        var sequenceIndex = projectSnapshot.TestSequenceVersions.FindIndex(candidate =>
            candidate.SequenceId == sequenceId && string.Equals(candidate.Version, sequenceVersion, StringComparison.Ordinal));
        projectSnapshot.TestSequenceVersions[sequenceIndex] = publishedSequence;

        var package = new PublishedConfigurationPackage
        {
            PackageId = CreatePackageId(sequence.SequenceId, sequence.Version),
            ProjectSnapshot = projectSnapshot,
            SequenceVersion = publishedSequence,
            ModelArtifactReferences = modelReferences,
            ContentHash = contentHash,
            CreatedAtUtc = createdAt,
            CreatedBy = createdBy
        };
        await _store.SavePublishedPackageAsync(package, cancellationToken);
        await _store.SaveDraftAsync(validated with
        {
            Project = ConfigurationContentHasher.DeepClone(projectSnapshot),
            SavedAtUtc = createdAt,
            Stage = ConfigurationLifecycleStage.Published,
            LastValidation = null
        }, cancellationToken);
        return package;
    }

    public static Guid CreatePackageId(Guid sequenceId, string sequenceVersion)
    {
        if (sequenceId == Guid.Empty)
        {
            throw new ArgumentException("Sequence ID 不能为空。", nameof(sequenceId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(sequenceVersion);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{sequenceId:D}\n{sequenceVersion}"));
        Span<byte> guidBytes = stackalloc byte[16];
        bytes.AsSpan(0, guidBytes.Length).CopyTo(guidBytes);
        guidBytes[7] = (byte)((guidBytes[7] & 0x0f) | 0x50);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3f) | 0x80);
        return new Guid(guidBytes);
    }

    public async Task<DeploymentAssignment> AssignAsync(
        Guid packageId,
        Guid deploymentBindingId,
        string assignedBy,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assignedBy);
        var package = await _store.LoadPublishedPackageAsync(packageId, cancellationToken)
            ?? throw new KeyNotFoundException($"Published Package {packageId} 不存在。");
        if (package.ProjectSnapshot.DeploymentBindings.All(binding =>
                binding.DeploymentBindingId != deploymentBindingId))
        {
            throw new InvalidOperationException("Package Snapshot 不包含目标 Deployment Binding。");
        }

        var assignment = new DeploymentAssignment
        {
            DeploymentBindingId = deploymentBindingId,
            PackageId = packageId,
            AssignedAtUtc = _timeProvider.GetUtcNow(),
            AssignedBy = assignedBy
        };
        await _store.SaveAssignmentAsync(assignment, cancellationToken);
        return assignment;
    }

    public async Task<ActiveDeploymentPointer> ActivateAsync(
        Guid packageId,
        Guid deploymentBindingId,
        string activatedBy,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activatedBy);
        var assignment = await _store.LoadAssignmentAsync(deploymentBindingId, cancellationToken)
            ?? throw new InvalidOperationException("Deployment 尚未 Assigned。");
        if (assignment.PackageId != packageId)
        {
            throw new InvalidOperationException("只能激活当前已 Assigned 的 Package。");
        }

        _ = await _store.LoadPublishedPackageAsync(packageId, cancellationToken)
            ?? throw new KeyNotFoundException($"Published Package {packageId} 不存在。");
        var current = await _store.LoadActivePointerAsync(deploymentBindingId, cancellationToken);
        var pointer = new ActiveDeploymentPointer
        {
            DeploymentBindingId = deploymentBindingId,
            PackageId = packageId,
            PreviousPackageId = current?.PackageId,
            ActivatedAtUtc = _timeProvider.GetUtcNow(),
            ActivatedBy = activatedBy
        };
        await _store.SaveActivePointerAsync(pointer, cancellationToken);
        return pointer;
    }

    public async Task<ActiveDeploymentPointer> RollbackAsync(
        Guid deploymentBindingId,
        Guid targetPackageId,
        string activatedBy,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activatedBy);
        _ = await _store.LoadPublishedPackageAsync(targetPackageId, cancellationToken)
            ?? throw new KeyNotFoundException($"Rollback 目标 Package {targetPackageId} 不存在。");
        var current = await _store.LoadActivePointerAsync(deploymentBindingId, cancellationToken)
            ?? throw new InvalidOperationException("Deployment 当前没有 Active Pointer，不能回滚。");
        if (current.PackageId == targetPackageId)
        {
            throw new InvalidOperationException("Rollback 目标已经是 Active Package。");
        }

        var pointer = new ActiveDeploymentPointer
        {
            DeploymentBindingId = deploymentBindingId,
            PackageId = targetPackageId,
            PreviousPackageId = current.PackageId,
            ActivatedAtUtc = _timeProvider.GetUtcNow(),
            ActivatedBy = activatedBy
        };
        await _store.SaveActivePointerAsync(pointer, cancellationToken);
        return pointer;
    }

    private static TestSequenceVersion ResolveSequence(
        ProjectConfigurationV2 project,
        Guid sequenceId,
        string sequenceVersion) =>
        project.TestSequenceVersions.SingleOrDefault(sequence =>
            sequence.SequenceId == sequenceId && string.Equals(sequence.Version, sequenceVersion, StringComparison.Ordinal))
        ?? throw new KeyNotFoundException($"Sequence {sequenceId} v{sequenceVersion} 不存在。");

    private static IReadOnlyList<ModelArtifactReference> ResolveModelReferences(
        ProjectConfigurationV2 project,
        TestSequenceVersion sequence)
    {
        var stepIds = sequence.OrderedInvocations.Select(invocation => invocation.StepId).ToHashSet();
        var artifactIds = project.TestStepCatalog.Where(step => stepIds.Contains(step.StepId))
            .SelectMany(step => step.ModelBindings)
            .Select(binding => binding.ModelArtifactId)
            .Distinct()
            .ToHashSet();
        return project.ModelArtifacts.Where(artifact => artifactIds.Contains(artifact.ModelArtifactId))
            .Select(artifact => new ModelArtifactReference(
                artifact.ModelArtifactId,
                artifact.Version,
                artifact.Sha256,
                artifact.AdapterId,
                artifact.RuntimeProfileId))
            .OrderBy(reference => reference.ModelArtifactId)
            .ToArray();
    }
}
