using VisualInspection.Core.V2.Configuration;
using VisualInspection.Core.V2.Execution;

namespace VisualInspection.Infrastructure.V2.Runtime;

public sealed class SequenceReadinessGate(
    IRuntimeConfigurationValidator validator,
    IModelRuntimeAdapterRegistry modelAdapters,
    string baseDirectory) : ISequenceReadinessGate
{
    private readonly IRuntimeConfigurationValidator _validator = validator;
    private readonly IModelRuntimeAdapterRegistry _modelAdapters = modelAdapters;
    private readonly string _baseDirectory = Path.GetFullPath(baseDirectory);

    public async Task<SequenceReadinessResult> EvaluateAsync(
        ProjectConfigurationV2 project,
        TestSequenceVersion sequence,
        DeploymentBinding deployment,
        RuntimeEnvironmentMode environmentMode,
        CancellationToken cancellationToken = default)
    {
        var schemaIssues = ProjectConfigurationV2Validator.Validate(project);
        var runtimeIssues = ProjectConfigurationV2Validator.HasErrors(schemaIssues)
            ? Array.Empty<V2ValidationIssue>()
            : await _validator.ValidateAsync(
                project,
                sequence,
                new RuntimeValidationContext
                {
                    EnvironmentMode = environmentMode,
                    BaseDirectory = _baseDirectory,
                    DeploymentBindingId = deployment.DeploymentBindingId
                },
                cancellationToken);
        var issues = schemaIssues.Concat(runtimeIssues).ToArray();
        var usedAdapterIds = project.TestStepCatalog
            .Where(step => sequence.OrderedInvocations.Any(invocation => invocation.StepId == step.StepId))
            .SelectMany(step => step.ModelBindings)
            .Select(binding => binding.AdapterId)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        var providers = usedAdapterIds.Select(adapterId =>
        {
            return _modelAdapters.TryGet(adapterId, out var adapter)
                ? new ProviderRecord(adapter.AdapterId, adapter.ProviderKind, adapter.SupportsHardCancellation)
                : new ProviderRecord(adapterId, RuntimeProviderKind.Unconfigured, false);
        }).ToArray();
        return new SequenceReadinessResult
        {
            IsReady = issues.All(issue => issue.Severity != V2ValidationSeverity.Error),
            Issues = issues,
            Providers = providers
        };
    }
}
