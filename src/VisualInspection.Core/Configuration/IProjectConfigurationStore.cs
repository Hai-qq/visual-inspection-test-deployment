namespace VisualInspection.Core.Configuration;

public interface IProjectConfigurationStore
{
    Task SaveAsync(ProjectConfiguration configuration, CancellationToken cancellationToken = default);

    Task<ProjectConfiguration?> LoadAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProjectConfigurationSummary>> ListAsync(CancellationToken cancellationToken = default);
}

public sealed record ProjectConfigurationSummary(
    Guid ProjectId,
    string ProjectName,
    int SchemaVersion,
    DateTimeOffset SavedAtUtc);
